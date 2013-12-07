﻿using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Public.Dac.Samples.App
{
    internal class ModelFilterExample
    {
        private static readonly string[] SampleScripts = new string[]
        {
            // Prod
            "CREATE SCHEMA [prod]",
            "CREATE TABLE [prod].[t1] (c1 INT NOT NULL PRIMARY KEY)",
            "CREATE VIEW [prod].[v1] AS SELECT c1 FROM [prod].[t1]",

            // Dev
            "CREATE SCHEMA [dev]",
            "CREATE TABLE [dev].[t2] (c2 INT NOT NULL PRIMARY KEY)",
            "CREATE VIEW [dev].[v2] AS SELECT c2 FROM [dev].[t2]",

            // Test - include reference to production table to highlight errors if filtering breaks references
            "CREATE SCHEMA [test]",
            "CREATE VIEW [test].[v3] AS SELECT c1 FROM [prod].[t1]",
        };

        private static void AddScriptsToModel(TSqlModel model, IEnumerable<string> scripts)
        {
            foreach (string script in scripts)
            {
                model.AddObjects(script);
            }
        }

        private static string GetFilePathInCurrentDirectory(string fileName)
        {
            return Path.Combine(Environment.CurrentDirectory, fileName);
        }

        /// <summary>
        /// Runs the model filtering example. This shows how to filter a model and save a new
        /// dacpac with the updated model. You can also update the model in the existing dacpac;
        /// the unit tests in TestFiltering.cs show how this is performed.
        /// </summary>
        public static void RunFilteringExample()
        {

            // Given a model with objects that use "dev", "test" and "prod" schemas
            string devPackagePath = GetFilePathInCurrentDirectory("dev.dacpac");
            var scripts = SampleScripts;
            using (TSqlModel model = new TSqlModel(SqlServerVersion.Sql110, new TSqlModelOptions()))
            {
                AddScriptsToModel(model, scripts);

                Console.WriteLine("Saving test scripts to package '"+devPackagePath+"'");
                DacPackageExtensions.BuildPackage(devPackagePath, model, new PackageMetadata());


                Console.WriteLine("Objects found in original package: '" + devPackagePath + "'");
                PrintTablesViewsAndSchemas(model);
                
            }

            string productionPackagePath = GetFilePathInCurrentDirectory("production.dacpac");

            // When saving a dacpac for deployment to production (filtering to exclude "dev" and "test" schemas)
            var schemaFilter = new SchemaBasedFilter("dev", "test");
            ModelFilterer modelFilterer = new ModelFilterer(schemaFilter);

            
            Console.WriteLine("Creating filtered 'production' package: '"+productionPackagePath+"'");
            modelFilterer.CreateFilteredDacpac(devPackagePath, productionPackagePath);

            // Then expect only the "prod" schema objects to remain in the new package
            using (TSqlModel filteredModel = new TSqlModel(productionPackagePath))
            {
                Console.WriteLine("Objects found in filtered package: '" + productionPackagePath + "'");
                PrintTablesViewsAndSchemas(filteredModel);
            }

            // If we publish the dacpac to a database, we can see that only the production schema is
            // present (debug into this method or view the console output listing only production elements)
            PublishProductionDacpacAndVerifyContents(productionPackagePath);
        }

        private static void PublishProductionDacpacAndVerifyContents(string productionPackagePath)
        {
            string extractedPackagePath = GetFilePathInCurrentDirectory("extracted.dacpac");
            using (DacPackage package = DacPackage.Load(productionPackagePath, DacSchemaModelStorageType.Memory))
            {
                Console.WriteLine("Deploying the production dacpac to 'ProductionDB'");
                DacServices services = new DacServices("Server=(localdb)\\v11.0;Integrated Security=true;");
                services.Deploy(package, "ProductionDB");

                Console.WriteLine("Extracting the 'ProductionDB' back to a dacpac for comparison");
                services.Extract(extractedPackagePath, "ProductionDB", "AppName", new Version(1, 0));
            }

            using (TSqlModel extractedModel = new TSqlModel(extractedPackagePath, DacSchemaModelStorageType.Memory))
            {
                Console.WriteLine("Objects found in extracted package: '" + productionPackagePath + "'");
                PrintTablesViewsAndSchemas(extractedModel);
            }
        }

        private static void PrintTablesViewsAndSchemas(TSqlModel model)
        {
            foreach (TSqlObject tsqlObject in model.GetObjects(DacQueryScopes.UserDefined,
                    Table.TypeClass,
                    View.TypeClass,
                    Schema.TypeClass))
            {
                Console.WriteLine("\t{0}", PrettyPrintObjectName(tsqlObject));
            }
        }

        /// <summary>
        /// Utility method that's unfortunately needed since public model doesn't currently return nicely formatted
        /// strings for an identifier
        /// </summary>
        /// <param name="tsqlObject"></param>
        /// <returns></returns>
        private static string PrettyPrintObjectName(TSqlObject tsqlObject)
        {
            StringBuilder name = new StringBuilder();
            ObjectIdentifier id = tsqlObject.Name;
            if(id.HasName)
            {
                // Models with references may contain objects with external name parts. 
                // These represent things like the "$(RefDatabase)" part of a name like [$(RefDatabase)].[MytSchema].[MyTable]
                if (id.HasExternalParts)
                {
                    foreach (string part in id.ExternalParts)
                    {
                        AddNamePart(name, part);
                    }
                }

                foreach (string part in id.Parts)
                {
                    AddNamePart(name, part);
                }
            }
            else
            {
                name.Append("UnnamedObject");
            }

            return name.ToString();
        }

        private static void AddNamePart(StringBuilder name, string part)
        {
            if (name.Length > 0)
            {
                name.Append('.');
            }
            name.Append('[').Append(part).Append(']');
        }

    }
}
