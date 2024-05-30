using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;

namespace TxtSqlExporter
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Build the configuration
            var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

            // Get the connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Use the connection string
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                Console.WriteLine("Connection opened successfully!");

                // Perform database operations
                ExportDatabaseObjects(connection, @"OUTPUT_PATH");
                connection.Close();
                Console.WriteLine("Connection closed successfully!");
            }
        }
        static void ExportDatabaseObjects(SqlConnection connection, string outputDirectory)
        {
            // Get the database name from the connection
            string databaseName = connection.Database;

            // Construct the output directory path with the database name
            string databaseOutputDirectory = Path.Combine(outputDirectory, databaseName, "tables");
            Directory.CreateDirectory(databaseOutputDirectory);

            // Export tables to the database-specific output directory
            ExportTables(connection, databaseOutputDirectory);

            // Construct the output directory path for views
            databaseOutputDirectory = Path.Combine(outputDirectory, databaseName, "views");
            Directory.CreateDirectory(databaseOutputDirectory);

            // Export views to the database-specific output directory
            ExportViews(connection, databaseOutputDirectory);

            // Construct the output directory path for storedprocedures
            databaseOutputDirectory = Path.Combine(outputDirectory, databaseName, "storedprocedures");
            Directory.CreateDirectory(databaseOutputDirectory);

            // Export storedprocedures to the database-specific output directory
            ExportStoredProcedures(connection, databaseOutputDirectory);

            // Construct the output directory path for functions
            databaseOutputDirectory = Path.Combine(outputDirectory, databaseName, "functions");
            Directory.CreateDirectory(databaseOutputDirectory);

            // Export functions to the database-specific output directory
            ExportFunctions(connection, databaseOutputDirectory);
        }

        static void ExportTables(SqlConnection connection, string outputDirectory)
        {
            string query = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            using (SqlCommand command = new SqlCommand(query, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string tableName = reader["TABLE_NAME"].ToString();
                    ExportTableScript(connection, tableName, outputDirectory);
                }
            }
        }

        static void ExportViews(SqlConnection connection, string outputDirectory)
        {
            string query = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS";
            using (SqlCommand command = new SqlCommand(query, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string viewName = reader["TABLE_NAME"].ToString();
                    ExportObjectDefinition(connection, viewName, "views", outputDirectory);
                }
            }
        }

        static void ExportStoredProcedures(SqlConnection connection, string outputDirectory)
        {
            string query = "SELECT SPECIFIC_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'";
            using (SqlCommand command = new SqlCommand(query, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string procedureName = reader["SPECIFIC_NAME"].ToString();
                    ExportObjectDefinition(connection, procedureName, "storedprocedures", outputDirectory);
                }
            }
        }

        static void ExportFunctions(SqlConnection connection, string outputDirectory)
        {
            string query = "SELECT SPECIFIC_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'FUNCTION'";
            using (SqlCommand command = new SqlCommand(query, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string functionName = reader["SPECIFIC_NAME"].ToString();
                    ExportObjectDefinition(connection, functionName, "functions", outputDirectory);
                }
            }
        }

        static void ExportObjectDefinition(SqlConnection connection, string objectName, string objectType, string outputDirectory)
        {
            string filePath = Path.Combine(outputDirectory, $"{objectName}.sql");
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                string query = $"EXEC sp_helptext '{objectName}'";
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        writer.Write(reader[0].ToString());
                    }
                }
            }

            Console.WriteLine($"{objectType} {objectName} has been exported to {filePath}");
        }
        static void ExportTableScript(SqlConnection connection, string tableName, string outputDirectory)
        {
            string script = GenerateTableScript(connection, tableName);
            string filePath = Path.Combine(outputDirectory, $"{tableName}.sql");
            File.WriteAllText(filePath, script);
            Console.WriteLine($"Table script for '{tableName}' has been exported to '{filePath}'.");
        }
        static string GenerateTableScript(SqlConnection connection, string tableName)
        {
            string query = $@"
                        DECLARE @sql NVARCHAR(MAX) = 'CREATE TABLE [{tableName}] (' + CHAR(13);
                        SELECT @sql = @sql +
                            '[' + COLUMN_NAME + '] ' + DATA_TYPE +
                            CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS NVARCHAR(10)) + ')' ELSE '' END +
                            CASE WHEN IS_NULLABLE = 'YES' THEN ' NULL' ELSE ' NOT NULL' END + -- Include NULL/NOT NULL
                            CASE WHEN IS_IDENTITY = 1 THEN ' IDENTITY(1,1)' ELSE '' END + ', ' + CHAR(13) -- Include IDENTITY
                        FROM (
                            SELECT 
                                COLUMN_NAME, 
                                DATA_TYPE, 
                                CASE WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(CHARACTER_MAXIMUM_LENGTH AS NVARCHAR(10)) END AS CHARACTER_MAXIMUM_LENGTH,
                                IS_NULLABLE,
                                COLUMNPROPERTY(OBJECT_ID(TABLE_NAME), COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY
                            FROM INFORMATION_SCHEMA.COLUMNS
                            WHERE TABLE_NAME = '{tableName}'
                        ) AS Columns;
                        SET @sql = LEFT(@sql, LEN(@sql) - 3) + CHAR(13) + ');';
                        SELECT @sql;";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                string script = command.ExecuteScalar()?.ToString();
                return script ?? string.Empty;
            }
        }
    }
}
