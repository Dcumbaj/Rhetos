﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using Oracle.ManagedDataAccess.Client;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Data.SqlClient;

namespace Rhetos.Utilities
{
    public static class SqlUtility
    {
        // TODO: Move most of the methods to ISqlUtility.

        private static int? _sqlCommandTimeout = null;
        /// <summary>
        /// In seconds.
        /// </summary>
        public static int SqlCommandTimeout
        {
            get
            {
                if (!_sqlCommandTimeout.HasValue)
                {
                    string value = ConfigUtility.GetAppSetting("SqlCommandTimeout");

                    if (!string.IsNullOrEmpty(value))
                        _sqlCommandTimeout = int.Parse(value);
                    else
                        _sqlCommandTimeout = 30;
                }
                return _sqlCommandTimeout.Value;
            }
        }

        private static string _databaseLanguage;
        private static string _nationalLanguage;

        private static void SetLanguageFromProviderName(string connectionStringProvider)
        {
            var match = new Regex(@"^Rhetos\.(?<DatabaseLanguage>\w+)(.(?<NationalLanguage>\w+))?$").Match(connectionStringProvider);
            if (!match.Success)
                throw new FrameworkException("Invalid 'providerName' format in 'ServerConnectionString' connection string. Expected providerName format is 'Rhetos.<database language>' or 'Rhetos.<database language>.<natural language settings>', for example 'Rhetos.MsSql' or 'Rhetos.Oracle.XGERMAN_CI'.");
            _databaseLanguage = match.Groups["DatabaseLanguage"].Value ?? "";
            _nationalLanguage = match.Groups["NationalLanguage"].Value ?? "";
        }

        private static string GetProviderNameFromConnectionString()
        {
            var connectionStringProvider = ConfigUtility.GetConnectionString().ProviderName;
            if (string.IsNullOrEmpty(connectionStringProvider))
                throw new FrameworkException("Missing 'providerName' attribute in 'ServerConnectionString' connection string. Expected providerName format is 'Rhetos.<database language>' or 'Rhetos.<database language>.<natural language settings>', for example 'Rhetos.MsSql' or 'Rhetos.Oracle.XGERMAN_CI'.");
            return connectionStringProvider;
        }

        public static string DatabaseLanguage
        {
            get
            {
                if (_databaseLanguage == null)
                    SetLanguageFromProviderName(GetProviderNameFromConnectionString());

                return _databaseLanguage;
            }
        }

        private static Lazy<bool> DatabaseLanguageIsMsSql = new Lazy<bool>(() => string.Equals(DatabaseLanguage, "MsSql", StringComparison.Ordinal));
        private static Lazy<bool> DatabaseLanguageIsOracle = new Lazy<bool>(() => string.Equals(DatabaseLanguage, "Oracle", StringComparison.Ordinal));

        public static string NationalLanguage
        {
            get
            {
                if (_nationalLanguage == null)
                    SetLanguageFromProviderName(GetProviderNameFromConnectionString());
                    
                return _nationalLanguage;
            }
        }


        private static string _connectionString;
        public static string ConnectionString
        {
            get
            {
                if (_connectionString == null)
                {
                    _connectionString = ConfigUtility.GetConnectionString().ConnectionString;
                    if (string.IsNullOrEmpty(_connectionString))
                        throw new FrameworkException("Empty 'ServerConnectionString' connection string in application configuration.");
                }
                return _connectionString;
            }
        }

        public static string ProviderName
        {
            get
            {
                if (DatabaseLanguageIsMsSql.Value)
                    return "System.Data.SqlClient";
                else if (DatabaseLanguageIsOracle.Value)
                    return "Oracle.ManagedDataAccess.Client";
                else
                    throw new FrameworkException(UnsupportedLanguageError);
            }
        }

        public static string UserContextInfoText(IUserInfo userInfo)
        {
            if (!userInfo.IsUserRecognized)
                return "";

            return "Rhetos:" + userInfo.Report();
        }

        public static IUserInfo ExtractUserInfo(string contextInfo)
        {
            if (contextInfo == null)
                return new ReconstructedUserInfo { IsUserRecognized = false, UserName = null, Workstation = null };

            string prefix1 = "Rhetos:";
            string prefix2 = "Alpha:";

            int positionUser;
            if (contextInfo.StartsWith(prefix1))
                positionUser = prefix1.Length;
            else if (contextInfo.StartsWith(prefix2))
                positionUser = prefix2.Length;
            else
                return new ReconstructedUserInfo { IsUserRecognized = false, UserName = null, Workstation = null };

            var result = new ReconstructedUserInfo();

            int positionWorkstation = contextInfo.IndexOf(',', positionUser);
            if (positionWorkstation > -1)
            {
                result.UserName = contextInfo.Substring(positionUser, positionWorkstation - positionUser);
                result.Workstation = contextInfo.Substring(positionWorkstation + 1);
            }
            else
            {
                result.UserName = contextInfo.Substring(positionUser);
                result.Workstation = "";
            }

            result.UserName = result.UserName.Trim();
            if (result.UserName == "") result.UserName = null;
            result.Workstation = result.Workstation.Trim();
            if (result.Workstation == "") result.Workstation = null;

            result.IsUserRecognized = result.UserName != null;
            return result;
        }

        private class ReconstructedUserInfo : IUserInfo
        {
            public bool IsUserRecognized { get; set; }
            public string UserName { get; set; }
            public string Workstation { get; set; }
            public string Report() { return UserName + "," + Workstation; }
        }

        /// <summary>
        /// Throws an exception if 'name' is not a valid SQL database object name.
        /// Function returns given argument so it can be used as fluent interface.
        /// In some cases the function may change the identifier (limit identifier length to 30 on Oracle database, e.g.).
        /// </summary>
        public static string Identifier(string name)
        {
            string error = CsUtility.GetIdentifierError(name);
            if (error != null)
                throw new FrameworkException("Invalid database object name: " + error);

            if (DatabaseLanguageIsOracle.Value)
                name = OracleSqlUtility.LimitIdentifierLength(name);
            if (DatabaseLanguageIsMsSql.Value)
                name = MsSqlUtility.LimitIdentifierLength(name);

            return name;
        }

        public static string QuoteText(string value)
        {
            return value != null
                ? "'" + value.Replace("'", "''") + "'"
                : "NULL";
        }

        public static string QuoteIdentifier(string sqlIdentifier)
        {
            if (SqlUtility.DatabaseLanguage == "MsSql")
            {
                sqlIdentifier = sqlIdentifier.Replace("]", "]]");
                return "[" + sqlIdentifier + "]";
            }
            throw new FrameworkException("Database language " + SqlUtility.DatabaseLanguage + " not supported.");
        }

        public static string GetSchemaName(string fullObjectName)
        {
            int dotPosition = fullObjectName.IndexOf('.');
            if (dotPosition == -1)
                if (DatabaseLanguageIsMsSql.Value)
                    return "dbo";
                else if (DatabaseLanguageIsOracle.Value)
                    throw new FrameworkException("Missing schema name for database object '" + fullObjectName + "'.");
                else
                    throw new FrameworkException(UnsupportedLanguageError);

            var schema = fullObjectName.Substring(0, dotPosition);
            return SqlUtility.Identifier(schema);
        }

        public static string GetShortName(string fullObjectName)
        {
            int dotPosition = fullObjectName.IndexOf('.');
            if (dotPosition == -1)
                return fullObjectName;

            var shortName = fullObjectName.Substring(dotPosition + 1);

            int secondDot = shortName.IndexOf('.');
            if (secondDot != -1 || string.IsNullOrEmpty(shortName))
                throw new FrameworkException("Invalid database object name: '" + fullObjectName + "'. Expected format is 'schema.name' or 'name'.");
            return SqlUtility.Identifier(shortName);
        }

        public static string GetFullName(string objectName)
        {
            var schema = SqlUtility.GetSchemaName(objectName);
            var name = SqlUtility.GetShortName(objectName);
            return schema + "." + name;
        }

        private static string UnsupportedLanguageError
        {
            get
            {
                return "SqlUtility functions are not supported for database language '" + DatabaseLanguage + "'."
                    + " Supported database languages are: 'MsSql', 'Oracle'.";
            }
        }

        /// <summary>
        /// Vendor-independent database reader.
        /// </summary>
        public static Guid ReadGuid(DbDataReader dataReader, int column)
        {
            if (DatabaseLanguageIsMsSql.Value)
                return dataReader.GetGuid(column);
            else if (DatabaseLanguageIsOracle.Value)
                return new Guid(((OracleDataReader)dataReader).GetOracleBinary(column).Value);
            else
                throw new FrameworkException(UnsupportedLanguageError);
        }

        /// <summary>
        /// Vendor-independent database reader.
        /// </summary>
        public static int ReadInt(DbDataReader dataReader, int column)
        {
            if (DatabaseLanguageIsMsSql.Value)
                return dataReader.GetInt32(column);
            else if (DatabaseLanguageIsOracle.Value)
                return Convert.ToInt32(dataReader.GetInt64(column)); // On some systems, reading from NUMERIC(10) column will return Int64, and GetInt32 would fail.
            else
                throw new FrameworkException(UnsupportedLanguageError);
        }

        public static Guid StringToGuid(string guid)
        {
            if (DatabaseLanguageIsMsSql.Value)
                return Guid.Parse(guid);
            else if (DatabaseLanguageIsOracle.Value)
                return new Guid(CsUtility.HexToByteArray(guid));
            else
                throw new FrameworkException(UnsupportedLanguageError);
        }

        public static string QuoteGuid(Guid guid)
        {
            return "'" + GuidToString(guid) + "'";
        }

        public static string QuoteGuid(Guid? guid)
        {
            return guid.HasValue
                ? "'" + GuidToString(guid.Value) + "'"
                : "NULL";
        }

        public static string GuidToString(Guid? guid)
        {
            return guid.HasValue ? GuidToString(guid.Value) : null;
        }

        public static string GuidToString(Guid guid)
        {
            if (DatabaseLanguageIsMsSql.Value)
                return guid.ToString().ToUpper();
            else if (DatabaseLanguageIsOracle.Value)
                return CsUtility.ByteArrayToHex(guid.ToByteArray());
            else
                throw new FrameworkException(UnsupportedLanguageError);
        }

        public static string QuoteDateTime(DateTime? dateTime)
        {
            return dateTime.HasValue
                ? "'" + DateTimeToString(dateTime.Value) + "'"
                : "NULL";
        }

        public static string DateTimeToString(DateTime? dateTime)
        {
            return dateTime.HasValue ? DateTimeToString(dateTime.Value) : null;
        }

        public static string DateTimeToString(DateTime dateTime)
        {
            return dateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff");
        }

        public static string QuoteBool(bool? b)
        {
            return b.HasValue ? BoolToString(b.Value) : "NULL";
        }

        public static string BoolToString(bool? b)
        {
            return b.HasValue ? BoolToString(b.Value) : null;
        }

        public static string BoolToString(bool b)
        {
            return b ? "0" : "1";
        }

        /// <summary>
        /// Returns empty string if the string value is null.
        /// This function is used for compatibility between MsSql and Oracle string behavior.
        /// </summary>
        public static string EmptyNullString(DbDataReader dataReader, int column)
        {
            if (DatabaseLanguageIsMsSql.Value)
                return dataReader.GetString(column) ?? "";
            else if (DatabaseLanguageIsOracle.Value)
            {
                var s = ((OracleDataReader)dataReader).GetOracleString(column);
                return !s.IsNull ? s.Value : "";
            }
            else
                throw new FrameworkException(UnsupportedLanguageError);
        }

        public static string SqlConnectionInfo(string connectionString)
        {
            SqlConnectionStringBuilder cs;
            try
            {
                cs = new SqlConnectionStringBuilder(connectionString);
            }
            catch
            {
                // This is not be a blocking error, because other database providers should be supported.
                return "(cannot parse connection string)";
            }

            var elements = new ListOfTuples<string, string>
            {
                { "DataSource", cs.DataSource },
                { "InitialCatalog", cs.InitialCatalog },
            };

            return
                string.Join(", ", elements
                    .Where(e => !string.IsNullOrWhiteSpace(e.Item2))
                    .Select(e => e.Item1 + "=" + e.Item2));
        }

        /// <summary>
        /// Used in DatabaseGenerator to split SQL script generated by IConceptDatabaseDefinition plugins.
        /// </summary>
        public const string ScriptSplitterTag = "/* database generator splitter */";

        /// <summary>
        /// Add this tag to the beginning of the DatabaseGenerator SQL script to execute it without transaction.
        /// Used for special database changes that must be executed without transaction, for example
        /// creating full-text search index.
        /// </summary>
        public const string NoTransactionTag = "/*DatabaseGenerator:NoTransaction*/";

        public static bool ScriptSupportsTransaction(string sql) => !sql.StartsWith(NoTransactionTag);

        public static DateTime GetDatabaseTime(ISqlExecuter sqlExecuter)
        {
            return DatabaseTimeCache.GetDatabaseTimeCached(() =>
            {
                DateTime databaseTime;
                if (DatabaseLanguageIsMsSql.Value)
                    databaseTime = MsSqlUtility.GetDatabaseTime(sqlExecuter);
                else if (DatabaseLanguageIsOracle.Value)
                    throw new FrameworkException("GetDatabaseTime function is not yet supported in Rhetos for Oracle database.");
                else
                    throw new FrameworkException(UnsupportedLanguageError);
                return DateTime.SpecifyKind(databaseTime, DateTimeKind.Local);
            }, () => DateTime.Now);
        }

        /// <summary>
        /// Splits the script to multiple batches, separated by the GO command.
        /// It emulates the behavior of Microsoft SQL Server utilities, sqlcmd and osql,
        /// but it does not work perfectly: comments near GO, strings containing GO and the repeat count are currently not supported.
        /// </summary>
        public static string[] SplitBatches(string sql)
        {
            return batchSplitter.Split(sql).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        private static readonly Regex batchSplitter = new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }
}
