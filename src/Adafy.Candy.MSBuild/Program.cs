using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NJsonSchema.Generation;
using NSwag.Commands;
using NSwag.Commands.Generation;
using NSwag.Generation.WebApi;
using Weikio.PluginFramework.Catalogs;
using Weikio.PluginFramework.Context;

// Default configuration, can not be customized for now
var documentSettings = new WebApiOpenApiDocumentGeneratorSettings
{
    IsAspNetCore = true,
    DefaultUrlTemplate = "{controller}/{id}",
    AddMissingPathParameters = false,
    DefaultResponseReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull,
    AllowNullableBodyParameters = true,
    SchemaSettings =
    {
        FlattenInheritanceHierarchy = false,
        GenerateAbstractProperties = false,
        GenerateAbstractSchemas = true,
        GenerateKnownTypes = true,
        GenerateXmlObjects = false,
        IgnoreObsoleteProperties = false,
        AllowReferencesWithProperties = false,
        DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.Null,
        DefaultDictionaryValueReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull
    }
};

var configurationFile = args[0];
Console.WriteLine($"Handling Nswag Configuration file: {configurationFile}");

var fullFilePath = new FileInfo(configurationFile);
var workingDir = fullFilePath.DirectoryName;

Console.WriteLine($"Working directory: {workingDir}");

var configurationFilePath = Path.Combine(workingDir, configurationFile);
var configuration = File.ReadAllText(configurationFilePath);

var nswagJson = JObject.Parse(configuration);

var outputType = nswagJson["documentGenerator"]["webApiToOpenApi"]["outputType"].Value<string>();
documentSettings.SchemaSettings.SchemaType = string.Equals("swagger2", outputType, StringComparison.InvariantCultureIgnoreCase) ? SchemaType.Swagger2 : SchemaType.OpenApi3;
documentSettings.Title = nswagJson["documentGenerator"]["webApiToOpenApi"]["infoTitle"].Value<string>();
documentSettings.Description = nswagJson["documentGenerator"]["webApiToOpenApi"]["infoDescription"].Value<string>();
documentSettings.Version = nswagJson["documentGenerator"]["webApiToOpenApi"]["infoVersion"].Value<string>();
var output = nswagJson["documentGenerator"]["webApiToOpenApi"]["output"].Value<string>();
var assemblyPaths =
    ((JArray)nswagJson["documentGenerator"]["webApiToOpenApi"]["assemblyPaths"]).Select(x => x.Value<string>());

// Load the controllers from the assemblies
var assemblyPlugins = new List<AssemblyPluginCatalog>();
foreach (var assemblyPath in assemblyPaths)
{
    var fullPath = Path.Combine(workingDir, assemblyPath);
    var assemblyFolder = Path.GetDirectoryName(fullPath);
    var assemblyReferencesFolder = Path.Combine(assemblyFolder, "References");

    if (!PluginLoadContextOptions.Defaults.AdditionalRuntimePaths.Contains(assemblyReferencesFolder))
    {
        // Make sure that PF can solve references from the References-folder
        PluginLoadContextOptions.Defaults.AdditionalRuntimePaths.Add(assemblyReferencesFolder);
    }
    
    var assemblyPlugin = new AssemblyPluginCatalog(fullPath, builder =>
    {
        builder.Inherits<ControllerBase>();
        builder.IsAbstract(false);
    });
    
    assemblyPlugins.Add(assemblyPlugin);
}

var compositePlugin = new CompositePluginCatalog(assemblyPlugins.ToArray());
await compositePlugin.Initialize();

var generator = new WebApiOpenApiDocumentGenerator(documentSettings);
var document = await generator.GenerateForControllersAsync(compositePlugin.GetPlugins().Select(x => x.Type));

var outputFilePath = Path.Combine(workingDir, output) ;
var documentJson = document.ToJson();
File.WriteAllText(outputFilePath, documentJson);

// Generate a temp nswag json configuration file which contains only the configured code generator
// Make sure to generate it to the same folder as the original nswag.json is
nswagJson.Remove("documentGenerator");
var tmpFilePath = Path.Combine(workingDir, Path.GetRandomFileName());

File.WriteAllText(tmpFilePath, nswagJson.ToString());

try
{
    var nswagDocument =
        await NSwagDocument.LoadAsync(tmpFilePath);

    nswagDocument.SelectedSwaggerGenerator = new FromDocumentCommand()
    {
        Json = documentJson
    };

    var res = await nswagDocument.ExecuteAsync();
    Console.WriteLine("Completed");
}
catch (Exception e)
{
    Console.WriteLine(e);
    throw;
}
finally
{
    // Cleanup
    try
    {
        File.Delete(tmpFilePath);
    }
    catch (Exception)
    {
        // ignored
    }
}

