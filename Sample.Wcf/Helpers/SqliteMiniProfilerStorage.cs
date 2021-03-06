﻿namespace Sample.Wcf.Helpers
{
    using System;
    using System.Linq;

    using Dapper;

    using StackExchange.Profiling;

    /// <summary>
    /// The SQLITE mini profiler storage.
    /// </summary>
    public class SqliteMiniProfilerStorage : StackExchange.Profiling.Storage.SqlServerStorage
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="SqliteMiniProfilerStorage"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        public SqliteMiniProfilerStorage(string connectionString)
            : base(connectionString)
        {
        }

        /// <summary>
        /// The get connection.
        /// </summary>
        /// <returns>the abstracted connection instance.</returns>
        protected override System.Data.Common.DbConnection GetConnection()
        {
            return new System.Data.SQLite.SQLiteConnection(ConnectionString);
        }

        /// <summary>
        /// load the profiler.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>the mini profiler /// </returns>
        public override MiniProfiler Load(Guid id)
        {
            // sqlite can't execute multiple result sets at once, so we need to override and run three queries
            MiniProfiler result;

            using (var conn = GetOpenConnection())
            {
                var param = new { id };
                result = conn.Query<MiniProfiler>("select * from MiniProfilerS where Id = @id", param).SingleOrDefault();

                if (result != null)
                {
                    // HACK: stored dates are utc, but are pulled out as local time - sqlite doesn't have dedicated datetime types, though
                    result.Started = new DateTime(result.Started.Ticks, DateTimeKind.Utc);

                    var timings = conn.Query<Timing>("select * from MiniProfilerTimings where MiniProfilerId = @id order by RowId", param).ToList();
                    var sqlTimings = conn.Query<SqlTiming>("select * from MiniProfilerSqlTimings where MiniProfilerId = @id order by RowId", param).ToList();
                    var sqlParameters = conn.Query<SqlTimingParameter>("select * from MiniProfilerSqlTimingParameters where MiniProfilerId = @id", param).ToList();
                    var clientTimingList = conn.Query<ClientTimings.ClientTiming>("select * from MiniProfilerClientTimings where MiniProfilerId = @id", param).ToList();

                    // TODO: This code is clearly not correct the timing list will cause a null pointer exception.
                    ClientTimings clientTimings = null;
                    if (clientTimingList.Count > 0)
                    {
                        clientTimings.Timings = clientTimingList;
                    }
                    MapTimings(result, timings, sqlTimings, sqlParameters, clientTimings);

                    // loading a profiler means we've viewed it
                    conn.Execute("update MiniProfilers set HasUserViewed = 1 where Id = @id", param);
                }
            }

            return result;
        }

        /// <summary>
        /// The table creation SQL.
        /// </summary>
        public static readonly string[] TableCreationSQL = new[] 
        { 
              @"create table MiniProfilers
              (
                 Id                                   uniqueidentifier not null primary key,
                 Name                                 nvarchar(200) not null,
                 Started                              datetime not null,
                 MachineName                          nvarchar(100) null,
                 [User]                               nvarchar(100) null,
                 Level                                tinyint null,
                 RootTimingId                         uniqueidentifier null,
                 DurationMilliseconds                 decimal(7, 1) not null,
                 DurationMillisecondsInSql            decimal(7, 1) null,
                 HasSqlTimings                        bit not null,
                 HasDuplicateSqlTimings               bit not null,
                 HasTrivialTimings                    bit not null,
                 HasAllTrivialTimings                 bit not null,
                 TrivialDurationThresholdMilliseconds decimal(5, 1) null,
                 HasUserViewed                        bit not null
              )", 
            
            @"create table MiniProfilerTimings
              (
                 RowId                               integer primary key autoincrement, -- sqlite: replace identity with autoincrement
                 Id                                  uniqueidentifier not null,
                 MiniProfilerId                      uniqueidentifier not null,
                 ParentTimingId                      uniqueidentifier null,
                 Name                                nvarchar(200) not null,
                 Depth                               smallint not null,
                 StartMilliseconds                   decimal(7, 1) not null,
                 DurationMilliseconds                decimal(7, 1) not null,
                 DurationWithoutChildrenMilliseconds decimal(7, 1) not null,
                 SqlTimingsDurationMilliseconds      decimal(7, 1) null,
                 IsRoot                              bit not null,
                 HasChildren                         bit not null,
                 IsTrivial                           bit not null,
                 HasSqlTimings                       bit not null,
                 HasDuplicateSqlTimings              bit not null,
                 ExecutedReaders                     smallint not null,
                 ExecutedScalars                     smallint not null,
                 ExecutedNonQueries                  smallint not null
              )",  
            @"create table MiniProfilerSqlTimings
              (
                 RowId                          integer primary key autoincrement, -- sqlite: replace identity with autoincrement
                 Id                             uniqueidentifier not null,
                 MiniProfilerId                 uniqueidentifier not null,
                 ParentTimingId                 uniqueidentifier not null,
                 ExecuteType                    tinyint not null,
                 StartMilliseconds              decimal(7, 1) not null,
                 DurationMilliseconds           decimal(7, 1) not null,
                 FirstFetchDurationMilliseconds decimal(7, 1) null,
                 IsDuplicate                    bit not null,
                 StackTraceSnippet              nvarchar(200) not null,
                 CommandString                  nvarchar not null -- sqlite: remove (max)
              )",   
            @"create table MiniProfilerSqlTimingParameters
              (
                 MiniProfilerId    uniqueidentifier not null,
                 ParentSqlTimingId uniqueidentifier not null,
                 Name              varchar(130) not null,
                 DbType            varchar(50) null,
                 Size              int null,
                 Value             nvarchar null -- sqlite: remove (max)
              )"
        
        };
    }
}