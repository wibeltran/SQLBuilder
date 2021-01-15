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

using Dapper;
using SQLBuilder.Diagnostics;
using SQLBuilder.Entry;
using SQLBuilder.Extensions;
using SQLBuilder.LoadBalancer;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SQLBuilder.Repositories
{
    /// <summary>
    /// 数据操作仓储抽象基类
    /// </summary>
    public abstract class BaseRepository
    {
        #region Field
        /// <summary>
        /// 诊断日志
        /// </summary>
        private static readonly DiagnosticListener _diagnosticListener =
            new DiagnosticListener(DiagnosticStrings.DiagnosticListenerName);
        #endregion

        #region Property
        /// <summary>
        /// 超时时长，默认240s
        /// </summary>
        public virtual int CommandTimeout { get; set; } = 240;

        /// <summary>
        /// 是否主库操作
        /// </summary>
        public virtual bool Master { get; set; } = true;

        /// <summary>
        /// 主库数据库连接字符串
        /// </summary>
        public virtual string MasterConnectionString { get; set; }

        /// <summary>
        /// 从库数据库连接字符串及权重集合
        /// </summary>
        public virtual (string connectionString, int weight)[] SlaveConnectionStrings { get; set; }

        /// <summary>
        /// 数据库连接对象
        /// </summary>
        public virtual DbConnection Connection { get; }

        /// <summary>
        /// 事务对象
        /// </summary>
        public virtual DbTransaction Transaction { get; set; }

        /// <summary>
        /// 是否启用对表名和列名格式化，默认不启用，注意：只针对Lambda表达式解析生成的sql
        /// </summary>
        public virtual bool IsEnableFormat { get; set; } = false;

        /// <summary>
        /// 分页计数语法，默认COUNT(*)
        /// </summary>
        public virtual string CountSyntax { get; set; } = "COUNT(*)";

        /// <summary>
        /// sql拦截委托
        /// </summary>
        public virtual Func<string, object, string> SqlIntercept { get; set; }

        /// <summary>
        /// 从库负载均衡接口
        /// </summary>
        public virtual ILoadBalancer LoadBalancer { get; set; }
        #endregion

        #region Transaction
        /// <summary>
        /// 开启事务
        /// </summary>
        /// <returns>IRepository</returns>
        public abstract IRepository BeginTrans();

        /// <summary>
        /// 关闭连接
        /// </summary>
        public abstract void Close();

        /// <summary>
        /// 提交事务
        /// </summary>
        public virtual void Commit()
        {
            Transaction?.Commit();
            Transaction?.Dispose();
            Close();
        }

        /// <summary>
        /// 回滚事务
        /// </summary>
        public virtual void Rollback()
        {
            Transaction?.Rollback();
            Transaction?.Dispose();
            Close();
        }

        /// <summary>
        /// 执行事务，内部自动开启事务、提交和回滚事务
        /// </summary>
        /// <param name="handler">自定义委托</param>
        /// <param name="rollback">事务回滚处理委托</param>
        public virtual void ExecuteTrans(Action<IRepository> handler, Action<Exception> rollback = null)
        {
            IRepository repository = null;
            try
            {
                if (handler != null)
                {
                    repository = BeginTrans();
                    handler(repository);
                    repository.Commit();
                }
            }
            catch (Exception ex)
            {
                repository?.Rollback();

                if (rollback != null)
                    rollback(ex);
                else
                    throw;
            }
        }

        /// <summary>
        /// 执行事务，根据自定义委托返回值内部自动开启事务、提交和回滚事务
        /// </summary>
        /// <param name="handler">自定义委托</param>
        /// <param name="rollback">事务回滚处理委托，注意：自定义委托返回false时，rollback委托的异常参数为null</param>
        public virtual bool ExecuteTrans(Func<IRepository, bool> handler, Action<Exception> rollback = null)
        {
            IRepository repository = null;
            try
            {
                if (handler != null)
                {
                    repository = BeginTrans();
                    var res = handler(repository);
                    if (res)
                        repository.Commit();
                    else
                    {
                        repository.Rollback();
                        rollback?.Invoke(null);
                    }

                    return res;
                }
            }
            catch (Exception ex)
            {
                repository?.Rollback();

                if (rollback != null)
                    rollback(ex);
                else
                    throw;
            }

            return false;
        }

        /// <summary>
        /// 执行事务，内部自动开启事务、提交和回滚事务
        /// </summary>
        /// <param name="handler">自定义委托</param>
        /// <param name="rollback">事务回滚处理委托</param>
        public virtual async Task ExecuteTransAsync(Func<IRepository, Task> handler, Func<Exception, Task> rollback = null)
        {
            IRepository repository = null;
            try
            {
                if (handler != null)
                {
                    repository = BeginTrans();
                    await handler(repository);
                    repository.Commit();
                }
            }
            catch (Exception ex)
            {
                repository?.Rollback();

                if (rollback != null)
                    await rollback(ex);
                else
                    throw;
            }
        }

        /// <summary>
        /// 执行事务，根据自定义委托返回值内部自动开启事务、提交和回滚事务
        /// </summary>
        /// <param name="handler">自定义委托</param>
        /// <param name="rollback">事务回滚处理委托，注意：自定义委托返回false时，rollback委托的异常参数为null</param>
        public virtual async Task<bool> ExecuteTransAsync(Func<IRepository, Task<bool>> handler, Func<Exception, Task> rollback = null)
        {
            IRepository repository = null;
            try
            {
                if (handler != null)
                {
                    repository = BeginTrans();
                    var res = await handler(repository);
                    if (res)
                        repository.Commit();
                    else
                    {
                        repository.Rollback();
                        await rollback?.Invoke(null);
                    }

                    return res;
                }
            }
            catch (Exception ex)
            {
                repository?.Rollback();

                if (rollback != null)
                    await rollback(ex);
                else
                    throw;
            }

            return false;
        }
        #endregion

        #region Page
        #region Sync
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
        public abstract string GetPageSql(bool isWithSyntax, string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex);

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public virtual (IEnumerable<T> list, long total) PageQuery<T>(SqlBuilderCore<T> builder, string orderField, bool isAscending, int pageSize, int pageIndex) where T : class
        {
            if (Transaction?.Connection != null)
            {
                var sqlQuery = GetPageSql(false, builder.Sql, builder.Parameters, orderField, isAscending, pageSize, pageIndex);
                return QueryMultiple<T>(Transaction.Connection, sqlQuery, builder.DynamicParameters, Transaction);
            }
            else
            {
                using var connection = Connection;
                var sqlQuery = GetPageSql(false, builder.Sql, builder.Parameters, orderField, isAscending, pageSize, pageIndex);
                return QueryMultiple<T>(connection, sqlQuery, builder.DynamicParameters);
            }
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public virtual (IEnumerable<T> list, long total) PageQuery<T>(bool isWithSyntax, string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            if (Transaction?.Connection != null)
            {
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return QueryMultiple<T>(Transaction.Connection, sqlQuery, parameter, Transaction);
            }
            else
            {
                using var connection = Connection;
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return QueryMultiple<T>(connection, sqlQuery, parameter);
            }
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public virtual (DataTable table, long total) PageQuery(bool isWithSyntax, string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            if (Transaction?.Connection != null)
            {
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return QueryMultiple(Transaction.Connection, sqlQuery, parameter, Transaction);
            }
            else
            {
                using var connection = Connection;
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return QueryMultiple(connection, sqlQuery, parameter);
            }
        }
        #endregion

        #region Async
        /// <summary>
        /// 分页查询
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public virtual async Task<(IEnumerable<T> list, long total)> PageQueryAsync<T>(SqlBuilderCore<T> builder, string orderField, bool isAscending, int pageSize, int pageIndex) where T : class
        {
            if (Transaction?.Connection != null)
            {
                var sqlQuery = GetPageSql(false, builder.Sql, builder.Parameters, orderField, isAscending, pageSize, pageIndex);
                return await QueryMultipleAsync<T>(Transaction.Connection, sqlQuery, builder.DynamicParameters, Transaction);
            }
            else
            {
                using var connection = Connection;
                var sqlQuery = GetPageSql(false, builder.Sql, builder.Parameters, orderField, isAscending, pageSize, pageIndex);
                return await QueryMultipleAsync<T>(connection, sqlQuery, builder.DynamicParameters);
            }
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public virtual async Task<(IEnumerable<T> list, long total)> PageQueryAsync<T>(bool isWithSyntax, string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            if (Transaction?.Connection != null)
            {
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return await QueryMultipleAsync<T>(Transaction.Connection, sqlQuery, parameter, Transaction);
            }
            else
            {
                using var connection = Connection;
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return await QueryMultipleAsync<T>(connection, sqlQuery, parameter);
            }
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序排序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns></returns>
        public virtual async Task<(DataTable table, long total)> PageQueryAsync(bool isWithSyntax, string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            if (Transaction?.Connection != null)
            {
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return await QueryMultipleAsync(Transaction.Connection, sqlQuery, parameter, Transaction);
            }
            else
            {
                using var connection = Connection;
                var sqlQuery = GetPageSql(isWithSyntax, sql, parameter, orderField, isAscending, pageSize, pageIndex);
                return await QueryMultipleAsync(connection, sqlQuery, parameter);
            }
        }
        #endregion
        #endregion

        #region Query
        #region Sync
        /// <summary>
        /// 查询集合数据
        /// </summary>
        /// <typeparam name="T">表实体类型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <returns></returns>
        public virtual IEnumerable<T> Query<T>(SqlBuilderCore<T> builder) where T : class
        {
            DiagnosticsMessage message = null;
            try
            {
                IEnumerable<T> result = null;

                var sql = builder.Sql;
                var parameter = builder.DynamicParameters;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.Query<T>(sql, parameter, Transaction, commandTimeout: CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.Query<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询集合数据
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual IEnumerable<T> Query<T>(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                IEnumerable<T> result = null;

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.Query<T>(sql, parameter, Transaction, commandTimeout: CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.Query<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询集合数据
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual DataTable Query(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                DataTable result = null;

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.ExecuteReader(sql, parameter, Transaction, CommandTimeout).ToDataTable();
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.ExecuteReader(sql, parameter, commandTimeout: CommandTimeout).ToDataTable();
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询首条数据
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <returns></returns>
        public virtual T QueryFirstOrDefault<T>(SqlBuilderCore<T> builder) where T : class
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = default(T);

                var sql = builder.Sql;
                var parameter = builder.DynamicParameters;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.QueryFirstOrDefault<T>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.QueryFirstOrDefault<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询首条数据
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual T QueryFirstOrDefault<T>(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = default(T);

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.QueryFirstOrDefault<T>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.QueryFirstOrDefault<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询多结果集数据
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual List<IEnumerable<dynamic>> QueryMultiple(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                var list = new List<IEnumerable<dynamic>>();

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    var result = Transaction.Connection.QueryMultiple(sql, parameter, Transaction, CommandTimeout);
                    while (result?.IsConsumed == false)
                    {
                        list.Add(result.Read());
                    }
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    var result = connection.QueryMultiple(sql, parameter, commandTimeout: CommandTimeout);
                    while (result?.IsConsumed == false)
                    {
                        list.Add(result.Read());
                    }
                }

                ExecuteAfter(message);

                return list;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询多结果集数据
        /// </summary>
        /// <param name="connection">数据库连接对像</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="transaction">事务</param>
        /// <returns></returns>
        public virtual (IEnumerable<T> list, long total) QueryMultiple<T>(DbConnection connection, string sql, object parameter, DbTransaction transaction = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                message = ExecuteBefore(sql, parameter, connection.DataSource);

                var multiQuery = connection.QueryMultiple(sql, parameter, transaction, CommandTimeout);
                var total = multiQuery?.ReadFirstOrDefault<long>() ?? 0;
                var list = multiQuery?.Read<T>();

                ExecuteAfter(message);

                return (list, total);
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询多结果集数据
        /// </summary>
        /// <param name="connection">数据库连接对像</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="transaction">事务</param>
        /// <returns></returns>
        public virtual (DataTable table, long total) QueryMultiple(DbConnection connection, string sql, object parameter, DbTransaction transaction = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                message = ExecuteBefore(sql, parameter, connection.DataSource);

                var multiQuery = connection.QueryMultiple(sql, parameter, transaction, CommandTimeout);
                var total = multiQuery?.ReadFirstOrDefault<long>() ?? 0;
                var table = multiQuery?.Read()?.ToList()?.ToDataTable();
                if (table?.Columns?.Contains("ROWNUMBER") == true)
                    table.Columns.Remove("ROWNUMBER");

                ExecuteAfter(message);

                return (table, total);
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }
        #endregion

        #region Async
        /// <summary>
        /// 查询集合数据
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>> QueryAsync<T>(SqlBuilderCore<T> builder) where T : class
        {
            DiagnosticsMessage message = null;
            try
            {
                IEnumerable<T> result = null;

                var sql = builder.Sql;
                var parameter = builder.DynamicParameters;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.QueryAsync<T>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.QueryAsync<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询集合数据
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>> QueryAsync<T>(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                IEnumerable<T> result = null;

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.QueryAsync<T>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.QueryAsync<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询集合数据
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual async Task<DataTable> QueryAsync(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                DataTable result = null;

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    var reader = await Transaction.Connection.ExecuteReaderAsync(sql, parameter, Transaction, CommandTimeout);
                    result = reader.ToDataTable();
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    var reader = await connection.ExecuteReaderAsync(sql, parameter, commandTimeout: CommandTimeout);
                    result = reader.ToDataTable();
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询首条数据
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <returns></returns>
        public virtual async Task<T> QueryFirstOrDefaultAsync<T>(SqlBuilderCore<T> builder) where T : class
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = default(T);

                var sql = builder.Sql;
                var parameter = builder.DynamicParameters;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.QueryFirstOrDefaultAsync<T>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.QueryFirstOrDefaultAsync<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询首条数据
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual async Task<T> QueryFirstOrDefaultAsync<T>(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = default(T);

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.QueryFirstOrDefaultAsync<T>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.QueryFirstOrDefaultAsync<T>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询多结果集数据
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual async Task<List<IEnumerable<dynamic>>> QueryMultipleAsync(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                var list = new List<IEnumerable<dynamic>>();

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    var result = await Transaction.Connection.QueryMultipleAsync(sql, parameter, Transaction, CommandTimeout);
                    while (result?.IsConsumed == false)
                    {
                        list.Add(await result.ReadAsync());
                    }
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    var result = await connection.QueryMultipleAsync(sql, parameter, commandTimeout: CommandTimeout);
                    while (result?.IsConsumed == false)
                    {
                        list.Add(await result.ReadAsync());
                    }
                }

                ExecuteAfter(message);

                return list;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询多结果集数据
        /// </summary>
        /// <param name="connection">数据库连接对像</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="transaction">事务</param>
        /// <returns></returns>
        public virtual async Task<(IEnumerable<T> list, long total)> QueryMultipleAsync<T>(DbConnection connection, string sql, object parameter, DbTransaction transaction = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                message = ExecuteBefore(sql, parameter, connection.DataSource);

                var multiQuery = await connection.QueryMultipleAsync(sql, parameter, transaction, CommandTimeout);
                var total = (long)((await multiQuery?.ReadFirstOrDefaultAsync<dynamic>())?.TOTAL ?? 0);
                var list = await multiQuery?.ReadAsync<T>();

                ExecuteAfter(message);

                return (list, total);
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 查询多结果集数据
        /// </summary>
        /// <param name="connection">数据库连接对像</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="transaction">事务</param>
        /// <returns></returns>
        public virtual async Task<(DataTable table, long total)> QueryMultipleAsync(DbConnection connection, string sql, object parameter, DbTransaction transaction = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                message = ExecuteBefore(sql, parameter, connection.DataSource);

                var multiQuery = await connection.QueryMultipleAsync(sql, parameter, transaction, CommandTimeout);
                var total = (long)((await multiQuery?.ReadFirstOrDefaultAsync<dynamic>())?.TOTAL ?? 0);
                var reader = await multiQuery?.ReadAsync();
                var table = reader?.ToList()?.ToDataTable();
                if (table?.Columns?.Contains("ROWNUMBER") == true)
                    table.Columns.Remove("ROWNUMBER");

                ExecuteAfter(message);

                return (table, total);
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }
        #endregion
        #endregion

        #region Execute
        #region Sync
        /// <summary>
        /// 执行sql
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <param name="command">命令类型</param>
        /// <returns></returns>
        public virtual int Execute<T>(SqlBuilderCore<T> builder, CommandType? command = null) where T : class
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = 0;

                var sql = builder.Sql;
                var parameter = builder.DynamicParameters;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.Execute(sql, parameter, Transaction, CommandTimeout, command);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.Execute(sql, parameter, null, CommandTimeout, command);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="command">命令类型</param>
        /// <returns></returns>
        public virtual int Execute(string sql, object parameter, CommandType? command = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = 0;

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.Execute(sql, parameter, Transaction, CommandTimeout, command);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.Execute(sql, parameter, null, CommandTimeout, command);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="command">命令类型</param>
        /// <returns></returns>
        public virtual IEnumerable<T> Execute<T>(string sql, object parameter, CommandType? command = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                IEnumerable<T> result = null;

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.Query<T>(sql, parameter, Transaction, commandTimeout: CommandTimeout, commandType: command);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.Query<T>(sql, parameter, null, commandTimeout: CommandTimeout, commandType: command);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 执行sql，查询首行首列数据
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual object ExecuteScalar(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                object result = null;

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = Transaction.Connection.ExecuteScalar<object>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = connection.ExecuteScalar<object>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }
        #endregion

        #region Async
        /// <summary>
        /// 执行sql
        /// </summary>
        /// <typeparam name="T">表实体泛型</typeparam>
        /// <param name="builder">SqlBuilder构建对象</param>
        /// <param name="command">命令类型</param>
        /// <returns></returns>
        public virtual async Task<int> ExecuteAsync<T>(SqlBuilderCore<T> builder, CommandType? command = null) where T : class
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = 0;

                var sql = builder.Sql;
                var parameter = builder.DynamicParameters;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.ExecuteAsync(sql, parameter, Transaction, CommandTimeout, command);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.ExecuteAsync(sql, parameter, null, CommandTimeout, command);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="command">命令类型</param>
        /// <returns></returns>
        public virtual async Task<int> ExecuteAsync(string sql, object parameter, CommandType? command = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                var result = 0;

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.ExecuteAsync(sql, parameter, Transaction, CommandTimeout, command);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.ExecuteAsync(sql, parameter, null, CommandTimeout, command);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <typeparam name="T">泛型类型</typeparam>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <param name="command">命令类型</param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>> ExecuteAsync<T>(string sql, object parameter, CommandType? command = null)
        {
            DiagnosticsMessage message = null;
            try
            {
                IEnumerable<T> result = null;

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.QueryAsync<T>(sql, parameter, Transaction, CommandTimeout, command);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.QueryAsync<T>(sql, parameter, commandTimeout: CommandTimeout, commandType: command);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }

        /// <summary>
        /// 执行sql，查询首行首列数据
        /// </summary>
        /// <param name="isWithSyntax">是否with语法</param>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">参数</param>
        /// <returns></returns>
        public virtual async Task<object> ExecuteScalarAsync(bool isWithSyntax, string sql, object parameter)
        {
            DiagnosticsMessage message = null;
            try
            {
                object result = null;

                if (isWithSyntax)
                    sql = $"{sql} SELECT * FROM T";

                sql = SqlIntercept?.Invoke(sql, parameter) ?? sql;

                if (Transaction?.Connection != null)
                {
                    message = ExecuteBefore(sql, parameter, Transaction.Connection.DataSource);
                    result = await Transaction.Connection.ExecuteScalarAsync<object>(sql, parameter, Transaction, CommandTimeout);
                }
                else
                {
                    using var connection = Connection;
                    message = ExecuteBefore(sql, parameter, connection.DataSource);
                    result = await connection.ExecuteScalarAsync<object>(sql, parameter, commandTimeout: CommandTimeout);
                }

                ExecuteAfter(message);

                return result;
            }
            catch (Exception ex)
            {
                ExecuteError(message, ex);
                throw;
            }
        }
        #endregion
        #endregion

        #region ExecuteBySql
        #region Sync
        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回受影响行数</returns>
        public virtual int ExecuteBySql(string sql)
        {
            return Execute(sql, null);
        }

        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual int ExecuteBySql(string sql, object parameter)
        {
            return Execute(sql, parameter);
        }

        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual int ExecuteBySql(string sql, params DbParameter[] dbParameter)
        {
            return Execute(sql, dbParameter.ToDynamicParameters());
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <returns>返回受影响行数</returns>
        public virtual int ExecuteByProc(string procName)
        {
            return Execute(procName, null, CommandType.StoredProcedure);
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual int ExecuteByProc(string procName, object parameter)
        {
            return Execute(procName, parameter, CommandType.StoredProcedure);
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual IEnumerable<T> ExecuteByProc<T>(string procName, object parameter)
        {
            return Execute<T>(procName, parameter, CommandType.StoredProcedure);
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual int ExecuteByProc(string procName, params DbParameter[] dbParameter)
        {
            return Execute(procName, dbParameter.ToDynamicParameters(), CommandType.StoredProcedure);
        }
        #endregion

        #region Async
        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<int> ExecuteBySqlAsync(string sql)
        {
            return await ExecuteAsync(sql, null);
        }

        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<int> ExecuteBySqlAsync(string sql, object parameter)
        {
            return await ExecuteAsync(sql, parameter);
        }

        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<int> ExecuteBySqlAsync(string sql, params DbParameter[] dbParameter)
        {
            return await ExecuteAsync(sql, dbParameter.ToDynamicParameters());
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<int> ExecuteByProcAsync(string procName)
        {
            return await ExecuteAsync(procName, null, CommandType.StoredProcedure);
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<int> ExecuteByProcAsync(string procName, object parameter)
        {
            return await ExecuteAsync(procName, parameter, CommandType.StoredProcedure);
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<IEnumerable<T>> ExecuteByProcAsync<T>(string procName, object parameter)
        {
            return await ExecuteAsync<T>(procName, parameter, CommandType.StoredProcedure);
        }

        /// <summary>
        /// 执行sql存储过程
        /// </summary>
        /// <param name="procName">存储过程名称</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回受影响行数</returns>
        public virtual async Task<int> ExecuteByProcAsync(string procName, params DbParameter[] dbParameter)
        {
            return await ExecuteAsync(procName, dbParameter.ToDynamicParameters(), CommandType.StoredProcedure);
        }
        #endregion
        #endregion

        #region FindObject
        #region Sync
        /// <summary>
        /// 查询单个对象
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回查询结果对象</returns>
        public virtual object FindObject(string sql)
        {
            return FindObject(sql, null);
        }

        /// <summary>
        /// 查询单个对象
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回查询结果对象</returns>
        public virtual object FindObject(string sql, object parameter)
        {
            return ExecuteScalar(false, sql, parameter);
        }

        /// <summary>
        /// 查询单个对象
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回查询结果对象</returns>
        public virtual object FindObject(string sql, params DbParameter[] dbParameter)
        {
            return ExecuteScalar(false, sql, dbParameter.ToDynamicParameters());
        }
        #endregion

        #region Async
        /// <summary>
        /// 查询单个对象
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回查询结果对象</returns>
        public virtual async Task<object> FindObjectAsync(string sql)
        {
            return await FindObjectAsync(sql, null);
        }

        /// <summary>
        /// 查询单个对象
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回查询结果对象</returns>
        public virtual async Task<object> FindObjectAsync(string sql, object parameter)
        {
            return await ExecuteScalarAsync(false, sql, parameter);
        }

        /// <summary>
        /// 查询单个对象
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回查询结果对象</returns>
        public virtual async Task<object> FindObjectAsync(string sql, params DbParameter[] dbParameter)
        {
            return await ExecuteScalarAsync(false, sql, dbParameter.ToDynamicParameters());
        }
        #endregion
        #endregion

        #region FindTable
        #region Sync
        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回DataTable</returns>
        public virtual DataTable FindTable(string sql)
        {
            return FindTable(sql, null);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual DataTable FindTable(string sql, object parameter)
        {
            return Query(false, sql, parameter);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual DataTable FindTable(string sql, params DbParameter[] dbParameter)
        {
            return Query(false, sql, dbParameter.ToDynamicParameters());
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual (DataTable table, long total) FindTable(string sql, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return FindTable(sql, null, orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual (DataTable table, long total) FindTable(string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return PageQuery(false, sql, parameter, orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual (DataTable table, long total) FindTable(string sql, DbParameter[] dbParameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return PageQuery(false, sql, dbParameter.ToDynamicParameters(), orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回DataTable</returns>
        public virtual DataTable FindTableByWith(string sql)
        {
            return FindTableByWith(sql, null);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual DataTable FindTableByWith(string sql, object parameter)
        {
            return Query(true, sql, parameter);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual DataTable FindTableByWith(string sql, params DbParameter[] dbParameter)
        {
            return Query(true, sql, dbParameter.ToDynamicParameters());
        }

        /// <summary>
        /// with语法分页查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual (DataTable table, long total) FindTableByWith(string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return PageQuery(true, sql, parameter, orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// with语法分页查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual (DataTable table, long total) FindTableByWith(string sql, DbParameter[] dbParameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return PageQuery(true, sql, dbParameter.ToDynamicParameters(), orderField, isAscending, pageSize, pageIndex);
        }
        #endregion

        #region Async
        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回DataTable</returns>
        public virtual async Task<DataTable> FindTableAsync(string sql)
        {
            return await FindTableAsync(sql, null);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual async Task<DataTable> FindTableAsync(string sql, object parameter)
        {
            return await QueryAsync(false, sql, parameter);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual async Task<DataTable> FindTableAsync(string sql, params DbParameter[] dbParameter)
        {
            return await QueryAsync(false, sql, dbParameter.ToDynamicParameters());
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns>返回DataTable和总记录数</returns>
        public virtual async Task<(DataTable table, long total)> FindTableAsync(string sql, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return await FindTableAsync(sql, null, orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns>返回DataTable和总记录数</returns>
        public virtual async Task<(DataTable table, long total)> FindTableAsync(string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return await PageQueryAsync(false, sql, parameter, orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>
        /// <returns>返回DataTable和总记录数</returns>
        public virtual async Task<(DataTable table, long total)> FindTableAsync(string sql, DbParameter[] dbParameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return await PageQueryAsync(false, sql, dbParameter.ToDynamicParameters(), orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回DataTable</returns>
        public virtual async Task<DataTable> FindTableByWithAsync(string sql)
        {
            return await FindTableByWithAsync(sql, null);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual async Task<DataTable> FindTableByWithAsync(string sql, object parameter)
        {
            return await QueryAsync(true, sql, parameter);
        }

        /// <summary>
        /// 根据sql语句查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回DataTable</returns>
        public virtual async Task<DataTable> FindTableByWithAsync(string sql, params DbParameter[] dbParameter)
        {
            return await QueryAsync(true, sql, dbParameter.ToDynamicParameters());
        }

        /// <summary>
        /// with语法分页查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual async Task<(DataTable table, long total)> FindTableByWithAsync(string sql, object parameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return await PageQueryAsync(true, sql, parameter, orderField, isAscending, pageSize, pageIndex);
        }

        /// <summary>
        /// with语法分页查询
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <param name="orderField">排序字段</param>
        /// <param name="isAscending">是否升序</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="pageIndex">当前页码</param>        
        /// <returns>返回DataTable和总记录数</returns>
        public virtual async Task<(DataTable table, long total)> FindTableByWithAsync(string sql, DbParameter[] dbParameter, string orderField, bool isAscending, int pageSize, int pageIndex)
        {
            return await PageQueryAsync(true, sql, dbParameter.ToDynamicParameters(), orderField, isAscending, pageSize, pageIndex);
        }
        #endregion
        #endregion

        #region FindMultiple
        #region Sync
        /// <summary>
        /// 根据sql语句查询返回多个结果集
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回查询结果集</returns>
        public virtual List<IEnumerable<dynamic>> FindMultiple(string sql)
        {
            return FindMultiple(sql, null);
        }

        /// <summary>
        /// 根据sql语句查询返回多个结果集
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回查询结果集</returns>
        public virtual List<IEnumerable<dynamic>> FindMultiple(string sql, object parameter)
        {
            return QueryMultiple(false, sql, parameter);
        }

        /// <summary>
        /// 根据sql语句查询返回多个结果集
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回查询结果集</returns>
        public virtual List<IEnumerable<dynamic>> FindMultiple(string sql, params DbParameter[] dbParameter)
        {
            return QueryMultiple(false, sql, dbParameter.ToDynamicParameters());
        }
        #endregion

        #region Async
        /// <summary>
        /// 根据sql语句查询返回多个结果集
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <returns>返回查询结果集</returns>
        public virtual async Task<List<IEnumerable<dynamic>>> FindMultipleAsync(string sql)
        {
            return await FindMultipleAsync(sql, null);
        }

        /// <summary>
        /// 根据sql语句查询返回多个结果集
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">对应参数</param>
        /// <returns>返回查询结果集</returns>
        public virtual async Task<List<IEnumerable<dynamic>>> FindMultipleAsync(string sql, object parameter)
        {
            return await QueryMultipleAsync(false, sql, parameter);
        }

        /// <summary>
        /// 根据sql语句查询返回多个结果集
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="dbParameter">对应参数</param>
        /// <returns>返回查询结果集</returns>
        public virtual async Task<List<IEnumerable<dynamic>>> FindMultipleAsync(string sql, params DbParameter[] dbParameter)
        {
            return await QueryMultipleAsync(false, sql, dbParameter.ToDynamicParameters());
        }
        #endregion
        #endregion

        #region Diagnostics
        /// <summary>
        /// 执行前诊断
        /// </summary>
        /// <param name="sql">sql语句</param>
        /// <param name="parameter">sql参数</param>
        /// <param name="dataSource">数据源</param>
        /// <returns></returns>
        public DiagnosticsMessage ExecuteBefore(string sql, object parameter, string dataSource)
        {
            if (_diagnosticListener.IsEnabled(DiagnosticStrings.BeforeExecute))
            {
                var message = new DiagnosticsMessage
                {
                    Sql = sql,
                    Parameters = parameter,
                    DataSource = dataSource,
                    Timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds,
                    Operation = DiagnosticStrings.BeforeExecute
                };

                _diagnosticListener.Write(DiagnosticStrings.BeforeExecute, message);

                return message;
            }

            return null;
        }

        /// <summary>
        /// 执行后诊断
        /// </summary>
        /// <param name="message">诊断消息</param>
        /// <returns></returns>
        public void ExecuteAfter(DiagnosticsMessage message)
        {
            if (message?.Timestamp != null && _diagnosticListener.IsEnabled(DiagnosticStrings.AfterExecute))
            {
                message.Operation = DiagnosticStrings.AfterExecute;
                message.ElapsedMilliseconds = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds - message.Timestamp.Value;

                _diagnosticListener.Write(DiagnosticStrings.AfterExecute, message);
            }
        }

        /// <summary>
        /// 执行异常诊断
        /// </summary>
        /// <param name="message">诊断消息</param>
        /// <param name="exception">异常</param>
        public void ExecuteError(DiagnosticsMessage message, Exception exception)
        {
            if (exception != null && message?.Timestamp != null && _diagnosticListener.IsEnabled(DiagnosticStrings.ErrorExecute))
            {
                message.Exception = exception;
                message.Operation = DiagnosticStrings.ErrorExecute;
                message.ElapsedMilliseconds = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds - message.Timestamp.Value;

                _diagnosticListener.Write(DiagnosticStrings.ErrorExecute, message);
            }
        }
        #endregion
    }
}
