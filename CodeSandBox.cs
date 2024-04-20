using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
/// <summary>
/// 通过传入Script，编译并执行
/// </summary>
public class CodeSandBox(List<Type> block_call_type=null):IDisposable
{
    static bool IsWindowsEnv = Environment.OSVersion.Platform == PlatformID.Win32NT;
    static CSharpCompilationOptions CompilationOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication).WithOptimizationLevel(OptimizationLevel.Release)
                                                                                                                    .WithPlatform(Platform.AnyCpu);
    static CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Parse, SourceCodeKind.Script);
    static SymbolEqualityComparer symbolEqualityComparer = SymbolEqualityComparer.Default;
    const string CandBox = "CandBox";
    const string CandBoxExe = "CandBox.exe";
    const string templatePath = "./Template/CandBox.runtimeconfig.json";
    const string templateFileName = "CandBox.runtimeconfig.json";
    readonly string Id = Guid.NewGuid().ToString();
    string outputPath;
    public string Script { get; set; }
    public string Output{get;private set;}
    public async void Run()
    {
        var tree = CSharpSyntaxTree.ParseText(Script, ParseOptions);
        
        var compilation = CSharpCompilation.Create(CandBox,[tree]);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        
        // var methodSymbols=from node in root.DescendantNodes().OfType<InvocationExpressionSyntax>()
        //     let symbol=model.GetSymbolInfo(node.Expression).Symbol as IMethodSymbol
        //     where symbol!=null
        //     select symbol;
            
        foreach(var s in root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(node => model.GetSymbolInfo(node.Expression).Symbol as IMethodSymbol)
            .Where(symbol => symbol != null))
        {
            var _method_receiver = s.ReceiverType;
            var _method_name = s.Name;

            if (block_call_type != null)
            {
                foreach(var t in block_call_type)
                {
                    if (TypeSymbolMatchesType(s.ContainingType, t, model))
                    {
                        Console.WriteLine($"{s} is blocked");
                        return;
                    }
                }
            }
        }
        
        // var 
        // 从使用的类型中自动添加引用
        // foreach(var node in root.DescendantNodes().OfType<TypeSyntax>())
        // {
        //     var symbol = model.GetSymbolInfo(node).Symbol;
        //     if(symbol is INamedTypeSymbol type)
        //     {
        //         if(type is not null && symbol.ContainingNamespace.ToDisplayString().StartsWith("System"))
        //         {
        //             var typeName = type.ToDisplayString();
        //             var clrType = Type.GetType(typeName);

        //         }
        //     }
        // }
        var usedAssembly = root.DescendantNodes()
            .OfType<TypeSyntax>().
            Select(s=>model.GetSymbolInfo(s).Symbol)
            .Where(s=>s is INamedTypeSymbol)
            .Where(s=>s.ContainingNamespace.ToDisplayString().StartsWith("System"))
            .Select(s=>s.ToDisplayString())
            .Select(s=>Type.GetType(s).Assembly.Location)
            .Distinct();
        // var references = usedAssembly.Select(s=>MetadataReference.CreateFromFile(s));
        // references.Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location)).ToList();
        var references = AppDomain.CurrentDomain.GetAssemblies().Where(x=>x.Location.Contains("System")).Select(x => {return MetadataReference.CreateFromFile(x.Location);});
        compilation = compilation.AddReferences(references);

        outputPath = Path.Combine(Path.GetTempPath(),Id);
        Directory.CreateDirectory(outputPath);
        // 把模板文件复制到输出目录
        File.Copy(templatePath,Path.Combine(outputPath,templateFileName),true);
        var outPutFilePaht = Path.Combine(outputPath,IsWindowsEnv?CandBoxExe:CandBox);
        var result = compilation.Emit(outPutFilePaht);
        if (!result.Success)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.WriteLine(diagnostic);
            }
        }
        else
        {
            System.Console.WriteLine($"编译成功,输出到{outPutFilePaht}");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = outPutFilePaht,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false
                }
            
            };
            process.OutputDataReceived+=(sender,args)=>{
                Output+=$"{args.Data}\n";
            };
            process.ErrorDataReceived+=(sender,args)=>{
                Output+=$"{args.Data}\n";
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if(process.WaitForExit(3000))
            {
                Console.WriteLine("运行成功");
            }
            else
            {
                Console.WriteLine("运行超时,中止");
                process.Kill();
            }

        }



    }

    /// <summary>
    /// 判断类型是否匹配
    /// </summary>
    /// <param name="typeSymbol"></param>
    /// <param name="blockedType"></param>
    /// <param name="semanticModel"></param>
    /// <returns></returns>
    static bool TypeSymbolMatchesType(ITypeSymbol typeSymbol, Type blockedType, SemanticModel semanticModel)
    {
        var blocked_type_symbol = GetTypeSymbolForType(blockedType, semanticModel);
        // return blocked_type_symbol.Equals(typeSymbol);
        return symbolEqualityComparer.Equals(blocked_type_symbol, typeSymbol);
    }
    /// <summary>
    /// 获取类型的符号
    /// </summary>
    static INamedTypeSymbol GetTypeSymbolForType(Type type, SemanticModel semanticModel)
    {
        if (!type.IsConstructedGenericType)
        {
            return semanticModel.Compilation.GetTypeByMetadataName(type.FullName);
        }
        var typeArgumentsTypeInfos = type.GenericTypeArguments.Select(a => GetTypeSymbolForType(a, semanticModel));

        var openType = type.GetGenericTypeDefinition();
        var typeSymbol = semanticModel.Compilation.GetTypeByMetadataName(openType.FullName);
        return typeSymbol.Construct(typeArgumentsTypeInfos.ToArray<ITypeSymbol>());
    }

    void IDisposable.Dispose()
    {
        if(Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath,true);
        }
    }
}