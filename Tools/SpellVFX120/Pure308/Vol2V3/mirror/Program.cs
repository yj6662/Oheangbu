using System;
using System.Text;
using Vol2V3.Tests;
namespace Vol2V3.Mirror
{
    // Runs every staged suite plus the S5-a suite against the Unity-side copies of the sources.
    public static class Program
    {
        static readonly string[] Suites = { "import", "grammar-budget", "reaction", "defence", "table-v11", "s5a" };
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length != 2 || args[0] != "--suite" || (args[1] != "all" && Array.IndexOf(Suites, args[1]) < 0))
            { Console.WriteLine("FAIL usage: --suite " + string.Join("|", Suites) + "|all"); return 1; }
            int passed = 0, failed = 0;
            try
            {
                using (var c = new Context())
                    foreach (string suite in Suites)
                    {
                        if (args[1] != "all" && args[1] != suite) continue;
                        var r = new Report(suite);
                        r.Run("suite", () =>
                        {
                            switch (suite)
                            {
                                case "import": Cases_Import.Run(r, c); break;
                                case "grammar-budget": Cases_GrammarBudget.Run(r, c); break;
                                case "reaction": Cases_Reaction.Run(r, c); break;
                                case "defence": Cases_Defence.Run(r, c); break;
                                case "table-v11": Cases_TableV11.Run(r, c); break;
                                default: Cases_S5a.Run(r, c); break;
                            }
                        });
                        r.Print(); passed += r.Passed; failed += r.Failed;
                    }
            }
            catch (Exception e) { Console.WriteLine("FAIL runner " + e); failed++; }
            Console.WriteLine("TOTAL: PASS " + passed + " / FAIL " + failed);
            return failed == 0 ? 0 : 1;
        }
    }
}
