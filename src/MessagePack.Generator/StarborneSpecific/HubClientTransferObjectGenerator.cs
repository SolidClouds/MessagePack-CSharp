using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Starborne;

[Generator]
public class HubClientTransferObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //#if DEBUG
        //		if (!Debugger.IsAttached)
        //		{
        //			Debugger.Launch();
        //		}
        //#endif

        // get the syntax provider
        var syntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InterfaceDeclarationSyntax i && i.Identifier.Text.EndsWith("HubClient"),
            static (node, _) => (
                Model: node.SemanticModel,
                Methods: node.Node.DescendantNodes().Where(i => i.IsKind(SyntaxKind.MethodDeclaration)).OfType<MethodDeclarationSyntax>()
                )
            );

        int index = 0;

        // register the source output
        context.RegisterSourceOutput(syntaxProvider, (context2, data) =>
        {
            if (!data.Methods.Any())
                return;

            // create a new StringBuilder to hold our generated code
            var sourceBuilder = new StringBuilder(@"
        using System.Threading.Tasks;
        using MessagePack;

        namespace Starborne.GeneratedCode;

        ");

            var unionBuilder = new StringBuilder();
            unionBuilder.AppendLine();


            string? IHubClient = null;

            string? interfaceName = null;
            foreach (var method in data.Methods)
            {
                if (method.Parent is not InterfaceDeclarationSyntax i)
                    continue;
                if (i.BaseList is null)
                    continue;
                if (i.BaseList.Types.FirstOrDefault(i => i.ToString() == "IHubClient") is not BaseTypeSyntax hubClientNode)
                    continue;

                IHubClient ??= GetDisplayString(hubClientNode.Type);

                var parameters = method.ParameterList.ChildNodes().OfType<ParameterSyntax>();

                int key = 0;
                sourceBuilder.AppendLine("[MessagePackObject]");
                sourceBuilder.AppendLine(@$"public record {method.Identifier}_TransferObject({string.Join(", ",
                    parameters.Select(p => $"[property: Key({key++})] {GetDisplayString(p.Type!)} {p.Identifier}"))}) : IClientTransferObject");

                interfaceName = i.Identifier.ToString();
                var nameSpace = i.SyntaxTree
                    .GetRoot()
                    .ChildNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Name
                    .ToString() is { } n ? n + "." : "";

                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine($"\tpublic Task PushToSub({IHubClient} hubClient)");
                sourceBuilder.AppendLine($"\t\t=> hubClient is {nameSpace + interfaceName} hub ? hub.{method.Identifier}({string.Join(", ", parameters.Select(i => i.Identifier))}) : Task.CompletedTask;");
                sourceBuilder.AppendLine("}");

                unionBuilder.AppendLine($"[Union({index++}, typeof({method.Identifier}_TransferObject))]");
            }

            if (!string.IsNullOrEmpty(interfaceName))
            {
                unionBuilder.AppendLine("public partial interface IClientTransferObject { }");
                sourceBuilder.Append(unionBuilder.ToString());
                context2.AddSource($"{interfaceName}_TransferObjects", sourceBuilder.ToString());
            }
            string GetDisplayString(SyntaxNode type)
            {
                var typeInfo = data.Model.GetTypeInfo(type!);
                var display = typeInfo.Type!.ToDisplayString();
                if (!display.EndsWith("?"))
                {
                    return display + (type is NullableTypeSyntax ? "?" : "");
                }
                else
                    return display;
            }
        });
    }
}
