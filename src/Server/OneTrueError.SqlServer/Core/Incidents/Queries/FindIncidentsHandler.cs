﻿using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using DotNetCqs;
using Griffin.Container;
using Griffin.Data;
using Griffin.Data.Mapper;
using OneTrueError.Api.Core.Incidents;
using OneTrueError.Api.Core.Incidents.Queries;

namespace OneTrueError.SqlServer.Core.Incidents.Queries
{
    [Component]
    public class FindIncidentsHandler : IQueryHandler<FindIncidents, FindIncidentResult>
    {
        private readonly IAdoNetUnitOfWork _uow;

        public FindIncidentsHandler(IAdoNetUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<FindIncidentResult> ExecuteAsync(FindIncidents query)
        {
            using (var cmd = (DbCommand) _uow.CreateCommand())
            {
                var sqlQuery = @"SELECT {0}
                                    FROM Incidents 
                                    JOIN Applications ON (Applications.Id = Incidents.ApplicationId)";
                if (query.ApplicationId > 0)
                {
                    sqlQuery += " WHERE ApplicationId = @id AND (";
                    cmd.AddParameter("id", query.ApplicationId);
                }
                else
                {
                    sqlQuery += "AND (";
                }

                if (query.Ignored)
                    sqlQuery += "IgnoreReports = 1 OR ";
                if (query.Closed)
                    sqlQuery += "IsSolved = 1 OR ";
                if (query.Open)
                    sqlQuery += "(IsSolved = 0 AND IgnoreReports = 0) OR ";
                if (query.ReOpened)
                    sqlQuery += "(IsReOpened = 1) OR ";

                if (sqlQuery.EndsWith("OR "))
                    sqlQuery = sqlQuery.Remove(sqlQuery.Length - 4) + ") ";
                else
                    sqlQuery = sqlQuery.Remove(sqlQuery.Length - 5);

                if (query.MinDate > DateTime.MinValue)
                {
                    sqlQuery += " AND Incidents.UpdatedAtUtc >= @minDate";
                    cmd.AddParameter("minDate", query.MinDate);
                }
                if (query.MaxDate < DateTime.MaxValue)
                {
                    sqlQuery += " AND Incidents.UpdatedAtUtc <= @maxDate";
                    cmd.AddParameter("maxDate", query.MaxDate);
                }

                //count first;
                cmd.CommandText = string.Format(sqlQuery, "count(Incidents.Id)");
                var count = await cmd.ExecuteScalarAsync();


                // then items
                if (query.SortType == IncidentOrder.Newest)
                {
                    if (query.SortAscending)
                        sqlQuery += " ORDER BY UpdatedAtUtc";
                    else
                        sqlQuery += " ORDER BY UpdatedAtUtc DESC";
                }
                else if (query.SortType == IncidentOrder.MostReports)
                {
                    if (query.SortAscending)
                        sqlQuery += " ORDER BY ReportCount";
                    else
                        sqlQuery += " ORDER BY ReportCount DESC";
                }
                cmd.CommandText = string.Format(sqlQuery,
                    "Incidents.*, Applications.Id as ApplicationId, Applications.Name as ApplicationName");
                if (query.PageNumber > 0)
                {
                    var offset = (query.PageNumber - 1)*query.ItemsPerPage;
                    cmd.CommandText += string.Format(@" OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY", offset,
                        query.ItemsPerPage);
                }
                var items = await cmd.ToListAsync<FindIncidentResultItem>();

                return new FindIncidentResult
                {
                    Items = items.ToArray(),
                    PageNumber = query.PageNumber,
                    PageSize = query.ItemsPerPage,
                    TotalCount = (int) count
                };
            }
        }
    }
}