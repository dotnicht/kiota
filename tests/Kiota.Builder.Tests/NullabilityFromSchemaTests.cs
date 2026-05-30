using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

using Moq;

using Xunit;
using NetHttpMethod = System.Net.Http.HttpMethod;

namespace Kiota.Builder.Tests;

/// <summary>
/// Tests that CreateProperty respects required/nullable from the OpenAPI schema.
/// Before the fix every property defaulted to IsNullable = true.
/// After the fix:
///   required + non-nullable  → IsNullable = false
///   required + nullable      → IsNullable = true
///   optional (any)           → IsNullable = true
/// </summary>
public sealed class NullabilityFromSchemaTests
{
    private readonly HttpClient _httpClient = new();

    // Helper: build a minimal document whose GET /items returns the given schema (by ref)
    private static (KiotaBuilder builder, CodeNamespace codeModel) Build(
        OpenApiSchema objectSchema,
        string schemaName = "item",
        string clientNamespace = "TestSdk")
    {
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["items"] = new OpenApiPathItem
                {
                    Operations = new()
                    {
                        [NetHttpMethod.Get] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Content = new Dictionary<string, IOpenApiMediaType>
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            Schema = new OpenApiSchemaReference(schemaName)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        document.AddComponent(schemaName, objectSchema);
        document.SetReferenceHostDocument();
        var logger = new Mock<ILogger<KiotaBuilder>>();
        var builder = new KiotaBuilder(
            logger.Object,
            new GenerationConfiguration
            {
                ClientClassName = "TestClient",
                ClientNamespaceName = clientNamespace,
                ApiRootUrl = "https://localhost"
            },
            new HttpClient());
        builder.SetOpenApiDocument(document);
        var node = builder.CreateUriSpace(document);
        var model = builder.CreateSourceModel(node);
        return (builder, model);
    }

    [Fact]
    public void RequiredNonNullable_IsNullableFalse()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["name"] = new OpenApiSchema { Type = JsonSchemaType.String }
            },
            Required = new HashSet<string> { "name" }
        };

        var (_, model) = Build(schema);

        var cls = model.FindChildByName<CodeClass>("item", true);
        Assert.NotNull(cls);
        var prop = cls.Properties.FirstOrDefault(p => p.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(prop);
        Assert.False(prop.Type.IsNullable, "required + non-nullable property must have IsNullable = false");
    }

    [Fact]
    public void RequiredNullable_IsNullableTrue()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                // type includes null → nullable even though required
                ["name"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null }
            },
            Required = new HashSet<string> { "name" }
        };

        var (_, model) = Build(schema);

        var cls = model.FindChildByName<CodeClass>("item", true);
        Assert.NotNull(cls);
        var prop = cls.Properties.FirstOrDefault(p => p.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(prop);
        Assert.True(prop.Type.IsNullable, "required + nullable (type|null) property must have IsNullable = true");
    }

    [Fact]
    public void OptionalNonNullable_IsNullableTrue()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["description"] = new OpenApiSchema { Type = JsonSchemaType.String }
            }
            // no Required set → description is optional
        };

        var (_, model) = Build(schema);

        var cls = model.FindChildByName<CodeClass>("item", true);
        Assert.NotNull(cls);
        var prop = cls.Properties.FirstOrDefault(p => p.Name.Equals("description", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(prop);
        Assert.True(prop.Type.IsNullable, "optional property must have IsNullable = true");
    }

    [Fact]
    public void OptionalNullable_IsNullableTrue()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["description"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null }
            }
        };

        var (_, model) = Build(schema);

        var cls = model.FindChildByName<CodeClass>("item", true);
        Assert.NotNull(cls);
        var prop = cls.Properties.FirstOrDefault(p => p.Name.Equals("description", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(prop);
        Assert.True(prop.Type.IsNullable, "optional + nullable property must have IsNullable = true");
    }

    [Fact]
    public void RequiredEnumProperty_IsNullableFalse()
    {
        var enumSchema = new OpenApiSchema
        {
            Title = "status",
            Type = JsonSchemaType.String,
            Enum = new List<JsonNode> { "active", "inactive" }
        };
        var objectSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["status"] = new OpenApiSchemaReference("status")
            },
            Required = new HashSet<string> { "status" }
        };

        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["items"] = new OpenApiPathItem
                {
                    Operations = new()
                    {
                        [NetHttpMethod.Get] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Content = new Dictionary<string, IOpenApiMediaType>
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            Schema = new OpenApiSchemaReference("item")
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        document.AddComponent("item", objectSchema);
        document.AddComponent("status", enumSchema);
        document.SetReferenceHostDocument();

        var logger = new Mock<ILogger<KiotaBuilder>>();
        var builder = new KiotaBuilder(
            logger.Object,
            new GenerationConfiguration
            {
                ClientClassName = "TestClient",
                ClientNamespaceName = "TestSdk",
                ApiRootUrl = "https://localhost"
            },
            _httpClient);
        builder.SetOpenApiDocument(document);
        var node = builder.CreateUriSpace(document);
        var model = builder.CreateSourceModel(node);

        var modelsNs = model.FindNamespaceByName("TestSdk.Models");
        Assert.NotNull(modelsNs);
        var cls = modelsNs.Classes.FirstOrDefault(c => c.Name.Equals("item", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(cls);
        var prop = cls.Properties.FirstOrDefault(p => p.Name.Equals("status", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(prop);
        Assert.False(prop.Type.IsNullable, "required enum property must have IsNullable = false (was broken before fix)");
    }

    [Fact]
    public void RequiredCollectionProperty_IsNullableFalse()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["tags"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            },
            Required = new HashSet<string> { "tags" }
        };

        var (_, model) = Build(schema);

        var cls = model.FindChildByName<CodeClass>("item", true);
        Assert.NotNull(cls);
        var prop = cls.Properties.FirstOrDefault(p => p.Name.Equals("tags", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(prop);
        Assert.False(prop.Type.IsNullable, "required collection property must have IsNullable = false");
    }

    [Fact]
    public void MultipleProperties_MixedRequiredNullability()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["nickname"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["score"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null }
            },
            Required = new HashSet<string> { "id", "score" }
        };

        var (_, model) = Build(schema);

        var cls = model.FindChildByName<CodeClass>("item", true);
        Assert.NotNull(cls);

        var idProp = cls.Properties.FirstOrDefault(p => p.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(idProp);
        Assert.False(idProp.Type.IsNullable, "required + non-nullable 'id' must have IsNullable = false");

        var nicknameProp = cls.Properties.FirstOrDefault(p => p.Name.Equals("nickname", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(nicknameProp);
        Assert.True(nicknameProp.Type.IsNullable, "optional 'nickname' must have IsNullable = true");

        var scoreProp = cls.Properties.FirstOrDefault(p => p.Name.Equals("score", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(scoreProp);
        Assert.True(scoreProp.Type.IsNullable, "required + nullable 'score' must have IsNullable = true");
    }
}
