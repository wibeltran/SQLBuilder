﻿#region License
/***
 * Copyright © 2018-2021, 张强 (943620963@qq.com).
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * without warranties or conditions of any kind, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#endregion

using SQLBuilder.Enums;
using SQLBuilder.Extensions;
using System;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;

namespace SQLBuilder.Repositories
{
    /// <summary>
    /// Sqlserver仓储实现类
    /// </summary>
    public class SqlRepository : BaseRepository
    {
        #region Field
        /// <summary>
        /// SqlServer数据库版本
        /// </summary>
        private int _serverVersion;
        #endregion

        #region Property
        /// <summary>
        /// 数据库连接对象
        /// </summary>
        /// <returns></returns>
        public override DbConnection Connection
        {
            get
            {
                SqlConnection connection;
                if (!Master && SlaveConnectionStrings?.Length > 0 && LoadBalancer != null)
                {
                    var connectionStrings = SlaveConnectionStrings.Select(x => x.connectionString);
                    var weights = SlaveConnectionStrings.Select(x => x.weight).ToArray();
                    var connectionString = LoadBalancer.Get(MasterConnectionString, connectionStrings, weights);

                    connection = new SqlConnection(connectionString);
                }
                else
                    connection = new SqlConnection(MasterConnectionString);

                if (connection.State != ConnectionState.Open)
                    connection.Open();

                //数据库版本
                _serverVersion = int.Parse(connection.ServerVersion.Split('.')[0]);

                return connection;
            }
        }

        /// <summary>
        /// 数据库类型
        /// </summary>
        public override DatabaseType DatabaseType => DatabaseType.SqlServer;

        /// <summary>
        /// 仓储接口
        /// </summary>
        public override IRepository Repository => this;
        #endregion

        #region Constructor
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="masterConnectionString">主库连接字符串，或者链接字符串名称</param>
        public SqlRepository(string masterConnectionString)
        {
            //判断是链接字符串，还是链接字符串名称
            MasterConnectionString = ConfigurationManager.ConnectionStrings[masterConnectionString]?.ConnectionString?.Trim();
            if (MasterConnectionString.IsNullOrEmpty())
                MasterConnectionString = ConfigurationManager.AppSettings[masterConnectionString]?.Trim();
            if (MasterConnectionString.IsNullOrEmpty())
                MasterConnectionString = masterConnectionString;
        }
        #endregion

        #region Page
        /// <summary>
        /// 获取分页语句
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">原始sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public override string GetPageSql(bool isWithSyntax, string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            //排序字段
            if (!orderField.IsNullOrEmpty())
            {
                if (orderField.Contains(@"(/\*(?:|)*?\*/)|(\b(ASC|DESC)\b)", RegexOptions.IgnoreCase))
                    orderField = $"ORDER BY {orderField}";
                else
                    orderField = $"ORDER BY {orderField} {(isAscending ? "ASC" : "DESC")}";
            }
            else
            {
                orderField = "ORDER BY (SELECT 0)";
            }

            string sqlQuery;
            var next = pageSize;
            var offset = pageSize * (pageIndex - 1);
            var rowStart = pageSize * (pageIndex - 1) + 1;
            var rowEnd = pageSize * pageIndex;

            //判断是否with语法
            if (isWithSyntax)
            {
                sqlQuery = $"{sql} SELECT {CountSyntax} AS [TOTAL] FROM T;";

                if (_serverVersion > 10)
                    sqlQuery += $"{sql} SELECT * FROM T {orderField} OFFSET {offset} ROWS FETCH NEXT {next} ROWS ONLY;";
                else
                    sqlQuery += $"{sql},R AS (SELECT ROW_NUMBER() OVER ({orderField}) AS [ROWNUMBER], * FROM T) SELECT * FROM R WHERE [ROWNUMBER] BETWEEN {rowStart} AND {rowEnd};";
            }
            else
            {
                sqlQuery = $"SELECT {CountSyntax} AS [TOTAL] FROM ({sql}) AS T;";

                if (_serverVersion > 10)
                    sqlQuery += $"SELECT * FROM ({sql}) AS T {orderField} OFFSET {offset} ROWS FETCH NEXT {next} ROWS ONLY;";
                else
                    sqlQuery += $"SELECT * FROM (SELECT ROW_NUMBER() OVER ({orderField}) AS [ROWNUMBER], * FROM ({sql}) AS T) AS N WHERE [ROWNUMBER] BETWEEN {rowStart} AND {rowEnd};";
            }

            sqlQuery = SqlIntercept?.Invoke(sqlQuery, parameter) ?? sqlQuery;

            return sqlQuery;
        }
        #endregion
    }
}