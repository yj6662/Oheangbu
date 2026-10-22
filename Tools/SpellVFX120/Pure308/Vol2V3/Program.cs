using System;
using System.Text;
using Vol2V3.Tests;
namespace Vol2V3
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding=new UTF8Encoding(false);
            if(args.Length!=2||args[0]!="--suite"||(args[1]!="import"&&args[1]!="grammar-budget"&&args[1]!="all"))
            { Console.WriteLine("FAIL usage: --suite import|grammar-budget|all");return 1; }
            int passed=0,failed=0;
            try
            {
                using(var c=new Context())
                {
                    foreach(string suite in new[]{"import","grammar-budget"})
                    {
                        if(args[1]!="all"&&args[1]!=suite)continue;
                        var r=new Report(suite);
                        r.Run("suite",()=>{if(suite=="import")Cases_Import.Run(r,c);else Cases_GrammarBudget.Run(r,c);});
                        r.Print();passed+=r.Passed;failed+=r.Failed;
                    }
                }
            }
            catch(Exception e){Console.WriteLine("FAIL runner "+e);failed++;}
            Console.WriteLine("TOTAL: PASS "+passed+" / FAIL "+failed);
            return failed==0?0:1;
        }
    }
}
