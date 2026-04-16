# Hermes — C2 Dashboard Educacional

## O que e um C2?

C2 (Command & Control) e o servidor central que um atacante usa para gerenciar maquinas comprometidas. O operador envia comandos pelo servidor, e os agentes (implants) instalados nas maquinas alvo executam esses comandos e devolvem o resultado. Entender esse fluxo e fundamental tanto para quem ataca (Red Team) quanto para quem defende (Blue Team).

## Por que este projeto?

Este projeto existe para construir um C2 funcional do zero com tecnologias acessíveis. Além disso, o objetivo é aprender na prática como funciona o fluxo de tasking, beaconing e comunicacao entre operador e agente, sem depender de ferramentas prontas.

## Arquitetura

```text
┌─────────────────────┐       ┌──────────────────┐       ┌───────────────┐
│   Frontend (HTML)   │──────>│  Express API      │──────>│  MongoDB      │
│   Dashboard Web     │<──────│  (REST endpoints) │<──────│  Atlas        │
└─────────────────────┘       └──────────────────┘       └───────────────┘
                                       ^
                               ┌───────┴────────┐
                               │  Agente C#      │
                               │  (.exe / ELF)   │
                               │  Executa cmds   │
                               └────────────────┘
```

1. O **agente C#** roda na maquina alvo e faz check-in periodico na API (beaconing)
2. O **operador** envia comandos pelo dashboard web
3. A **API Express** armazena os comandos como tarefas pendentes no MongoDB
4. O agente busca as tarefas, executa no sistema e devolve o output
5. O dashboard exibe o resultado em tempo real

## Stack

| Componente | Tecnologia                                     |
| ---------- | ---------------------------------------------- |
| Servidor   | Node.js + Express + MongoDB                    |
| Agente     | C# (.NET) — compila para executavel standalone |
| Frontend   | HTML / CSS / JavaScript                        |

## Pre-requisitos

- Node.js 18+
- .NET SDK 8+
- MongoDB local ou Atlas

```bash
node --version
npm --version
dotnet --version
```

## Configuracao

1. Instale as dependencias:

```bash
npm install
```

2. Crie o arquivo de ambiente:

```bash
cp .env.example .env
```

3. Edite o `.env` com os valores reais:

```env
PORT=3000
MONGODB_URI=mongodb://localhost:27017/c2dashboard
API_KEY=troque-esta-chave
USE_HTTPS=true
TLS_KEY_PATH=certs/server.key
TLS_CERT_PATH=certs/server.cert
```

4. Gere os certificados TLS (necessario para HTTPS):

```bash
bash scripts/generate-certs.sh
```

## Executar

**Terminal 1 — Servidor:**

```bash
npm run dev
```

**Terminal 2 — Agente:**

```bash
cd agent-csharp
dotnet run
```

## **Dashboard:**

<http://localhost:3000>

<img width="937" height="602" alt="Screenshot from 2026-04-13 16-51-34" src="https://github.com/user-attachments/assets/4fb9dc31-5b8d-470a-b4ce-ff2abee38ff4" />


## Compilar o agente para outra maquina

Para gerar um executavel standalone que roda sem precisar do .NET instalado:

```bash
cd agent-csharp

# Windows
dotnet publish -c Release -r win-x64 --self-contained true

# Linux
dotnet publish -c Release -r linux-x64 --self-contained true
```

O binario fica em `bin/Release/net<versao>/<runtime>/publish/`.

Antes de compilar, edite o `SERVER_URL` em `Program.cs` para apontar para o IP do servidor (em vez de `localhost`).

## Estrutura do projeto

```text
.
├── agent-csharp/          # Agente C# (implant)
│   ├── agent-csharp.csproj
│   └── Program.cs
├── certs/                 # Certificados TLS (gerados, nao commitados)
├── client/                # Dashboard do operador
│   ├── css/style.css
│   ├── js/app.js
│   └── index.html
├── scripts/               # Scripts auxiliares
│   └── generate-certs.sh  # Gera certificado self-signed para HTTPS
├── server/                # API Express
│   ├── config/db.js
│   ├── middleware/auth.js
│   ├── models/
│   ├── routes/
│   ├── utils/
│   │   └── crypto.js      # ECDH key exchange + AES-256-GCM encrypt/decrypt
│   └── index.js
├── outputs/               # Loot — arquivos exfiltrados dos agentes
├── .env.example
├── package.json
└── README.md
```

## Comandos internos do agente

O agente possui comandos built-in que sao resolvidos internamente, sem criar processos no sistema alvo:

| Comando | Uso | Descricao |
| ------- | --- | --------- |
| `cd` | `cd /tmp` | Navega entre diretorios no alvo. Suporta caminhos relativos, absolutos e `~` |
| `pwd` | `pwd` | Retorna o diretorio atual do agente |
| `sleep` | `sleep 10 30` | Altera o beacon: base em segundos e jitter em %. Ex: `sleep 10 30` = 7s a 13s. Padrão 5 - 50% |
| `download` | `download /etc/passwd` | Exfiltra um arquivo do alvo para a pasta `outputs/` no servidor |
| `upload` | `upload /tmp/payload.bin` | Envia um arquivo do operador para o alvo. O dashboard exibe um file picker automaticamente |

## Endpoints

| Metodo | Endpoint                      | Descricao                      | Auth |
| ------ | ----------------------------- | ------------------------------ | ---- |
| POST   | `/api/agents/register`        | Agente faz check-in            | Nao  |
| GET    | `/api/agents`                 | Listar agentes                 | Sim  |
| GET    | `/api/agents/:id`             | Detalhe de um agente           | Sim  |
| DELETE | `/api/agents/:id`             | Remover agente                 | Sim  |
| POST   | `/api/tasks`                  | Criar tarefa (comando)         | Sim  |
| GET    | `/api/tasks/agent/:agentId`   | Agente busca tarefas pendentes | Nao  |
| PUT    | `/api/tasks/:id/result`       | Agente envia resultado         | Nao  |
| GET    | `/api/tasks/history/:agentId` | Historico de tarefas           | Sim  |

Endpoints protegidos exigem o header `x-api-key`.

## Conceitos de Offensive Security

| Conceito            | Onde aparece no projeto                                             |
| ------------------- | ------------------------------------------------------------------- |
| **Beaconing**       | Loop do agente com `Task.Delay` + jitter aleatorio                  |
| **Tasking**         | Fluxo operador -> servidor -> agente -> servidor                    |
| **OPSEC**           | Auth por API key, token por agente, `CreateNoWindow`                |
| **Jitter**          | Intervalo variavel entre beacons, configuravel via tasking          |
| **Exfiltration**    | Download de arquivos do alvo em base64 para o servidor              |
| **File Upload**     | Upload de arquivos do operador para o alvo via stdin base64         |
| **Implant**         | Agente C# compila para executavel standalone                        |
| **Encrypted Comms** | HTTPS transport + AES-256-GCM payload com ECDH key exchange (T1573) |

## Roadmap

- [x] WebSockets (Socket.io) para atualizar o dashboard em tempo real
- [x] Suporte a PowerShell alem de cmd.exe
- [x] Upload/download de arquivos
- [x] Jitter configuravel via tasking do servidor
- [x] HTTPS com certificado self-signed
- [x] Comunicacao criptografada (AES-256-GCM + ECDH key exchange)
- [ ] Persistencia via Task Scheduler / Registry / Startup Folder
- [ ] Autenticacao JWT no dashboard

## Aviso Legal

Este projeto foi criado exclusivamente para fins educacionais e de pesquisa em seguranca ofensiva. Deve ser utilizado apenas em ambientes controlados e com autorizacao explicita (lab pessoal, VMs isoladas, ambientes de teste).

O autor nao se responsabiliza por qualquer uso indevido ou ilegal deste software. O uso contra sistemas sem autorizacao e crime previsto em lei.
