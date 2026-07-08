using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrudApp.StressTest
{
    public class StressOptions
    {
        public string Target { get; set; } = "http://localhost:5000";
        public string Mode { get; set; } = "help";
        public int Megabytes { get; set; } = 480;
        public int DurationSeconds { get; set; } = 300;
        public int Requests { get; set; } = 2000;
        public int Concurrency { get; set; } = 50;
        public int PayloadSize { get; set; } = 5000;
    }

    public static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static async Task<int> Main(string[] args)
        {
            var options = ParseArgs(args);

            if (options.Mode == "help")
            {
                PrintHelp();
                return 0;
            }

            var baseUrl = options.Target.TrimEnd('/');
            Console.WriteLine($"Target: {baseUrl}");
            Console.WriteLine($"Mode:   {options.Mode}");
            Console.WriteLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            return options.Mode switch
            {
                "memory" => await RunMemoryStressAsync(http, baseUrl, options),
                "api" => await RunApiStressAsync(http, baseUrl, options),
                "all" => await RunAllStressAsync(http, baseUrl, options),
                _ => PrintUnknownMode(options.Mode)
            };
        }

        private static async Task<int> RunAllStressAsync(HttpClient http, string baseUrl, StressOptions options)
        {
            Console.WriteLine("=== Fase 1: stress de API ===");
            var apiResult = await RunApiStressAsync(http, baseUrl, options);
            if (apiResult != 0)
            {
                return apiResult;
            }

            Console.WriteLine();
            Console.WriteLine("=== Fase 2: stress de memória ===");
            return await RunMemoryStressAsync(http, baseUrl, options);
        }

        private static async Task<int> RunMemoryStressAsync(HttpClient http, string baseUrl, StressOptions options)
        {
            var payload = JsonSerializer.Serialize(new
            {
                megabytes = options.Megabytes,
                durationSeconds = options.DurationSeconds
            }, JsonOptions);

            Console.WriteLine($"Alocando {options.Megabytes}MB por {options.DurationSeconds}s via POST /api/stress/memory ...");

            var response = await http.PostAsync(
                $"{baseUrl}/api/stress/memory",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
            Console.WriteLine(body);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine();
                Console.WriteLine("Dica: habilite StressTest:Enabled=true na aplicação alvo (Development ou env StressTest__Enabled=true).");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("Monitorar HPA:");
            Console.WriteLine("  oc get hpa -n dotnet-builders");
            Console.WriteLine("  oc adm top pods -n dotnet-builders");
            Console.WriteLine();
            Console.WriteLine("Parar antes do timeout:");
            Console.WriteLine($"  curl -X DELETE {baseUrl}/api/stress/memory");

            return 0;
        }

        private static async Task<int> RunApiStressAsync(HttpClient http, string baseUrl, StressOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            var success = 0;
            var failed = 0;
            var createdIds = new List<int>();
            var lockObj = new object();

            Console.WriteLine($"Requests: {options.Requests}, Concurrency: {options.Concurrency}, Payload: {options.PayloadSize} chars");

            using var semaphore = new SemaphoreSlim(options.Concurrency);
            var tasks = new List<Task>();

            for (var i = 0; i < options.Requests; i++)
            {
                var index = i;
                await semaphore.WaitAsync();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var name = new string('x', options.PayloadSize) + index;
                        var payload = JsonSerializer.Serialize(new
                        {
                            name,
                            price = 1.0m,
                            quantity = 1
                        }, JsonOptions);

                        var response = await http.PostAsync(
                            $"{baseUrl}/api/products",
                            new StringContent(payload, Encoding.UTF8, "application/json"));

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("id", out var idProp))
                            {
                                lock (lockObj)
                                {
                                    createdIds.Add(idProp.GetInt32());
                                    success++;
                                }
                            }
                            else
                            {
                                Interlocked.Increment(ref success);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref failed);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref failed);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            Console.WriteLine();
            Console.WriteLine($"Concluído em {stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"Sucesso: {success}");
            Console.WriteLine($"Falhas:  {failed}");
            Console.WriteLine($"Produtos criados (amostra): {Math.Min(createdIds.Count, 5)} ids");

            return failed > 0 && success == 0 ? 1 : 0;
        }

        private static StressOptions ParseArgs(string[] args)
        {
            var options = new StressOptions();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var next = i + 1 < args.Length ? args[i + 1] : null;

                switch (arg)
                {
                    case "--target":
                    case "-t":
                        options.Target = next ?? options.Target;
                        i++;
                        break;
                    case "--mode":
                    case "-m":
                        options.Mode = (next ?? options.Mode).ToLowerInvariant();
                        i++;
                        break;
                    case "--megabytes":
                    case "--mb":
                        options.Megabytes = int.Parse(next ?? "480");
                        i++;
                        break;
                    case "--duration":
                    case "-d":
                        options.DurationSeconds = int.Parse(next ?? "300");
                        i++;
                        break;
                    case "--requests":
                    case "-r":
                        options.Requests = int.Parse(next ?? "2000");
                        i++;
                        break;
                    case "--concurrency":
                    case "-c":
                        options.Concurrency = int.Parse(next ?? "50");
                        i++;
                        break;
                    case "--payload":
                        options.PayloadSize = int.Parse(next ?? "5000");
                        i++;
                        break;
                    case "--help":
                    case "-h":
                        options.Mode = "help";
                        break;
                }
            }

            return options;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("CrudApp.StressTest — simula carga para dotnet5-application");
            Console.WriteLine();
            Console.WriteLine("Uso:");
            Console.WriteLine("  dotnet run --project CrudApp.StressTest -- [opções]");
            Console.WriteLine();
            Console.WriteLine("Opções:");
            Console.WriteLine("  -t, --target URL        URL base (default: http://localhost:5000)");
            Console.WriteLine("  -m, --mode MODE         memory | api | all | help");
            Console.WriteLine("      --mb MEGABYTES      Memória para stress (default: 480)");
            Console.WriteLine("  -d, --duration SEC      Duração do stress de memória (default: 300)");
            Console.WriteLine("  -r, --requests N        Requisições POST no modo api (default: 2000)");
            Console.WriteLine("  -c, --concurrency N     Concorrência no modo api (default: 50)");
            Console.WriteLine("      --payload SIZE      Tamanho do nome do produto em chars (default: 5000)");
            Console.WriteLine();
            Console.WriteLine("Exemplos:");
            Console.WriteLine("  dotnet run --project CrudApp.StressTest -- -m memory --mb 480 -d 300");
            Console.WriteLine("  dotnet run --project CrudApp.StressTest -- -m api -r 5000 -c 100");
            Console.WriteLine("  dotnet run --project CrudApp.StressTest -- -t https://sua-route -m all");
        }

        private static int PrintUnknownMode(string mode)
        {
            Console.WriteLine($"Modo desconhecido: {mode}");
            PrintHelp();
            return 1;
        }
    }
}
