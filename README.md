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
├── client/                # Dashboard do operador
│   ├── css/style.css
│   ├── js/app.js
│   └── index.html
├── server/                # API Express
│   ├── config/db.js
│   ├── middleware/auth.js
│   ├── models/
│   ├── routes/
│   └── index.js
├── .env.example
├── package.json
└── README.md
```

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

| Conceito         | Onde aparece no projeto                                        |
| ---------------- | -------------------------------------------------------------- |
| **Beaconing**    | Loop do agente com `Thread.Sleep` + jitter aleatorio           |
| **Tasking**      | Fluxo operador -> servidor -> agente -> servidor               |
| **OPSEC**        | Auth por API key, token por agente, `CreateNoWindow`           |
| **Jitter**       | Intervalo variavel entre beacons dificulta deteccao por padrao |
| **Exfiltration** | Output dos comandos enviado de volta ao servidor               |
| **Implant**      | Agente C# compila para executavel standalone                   |

## Roadmap

- [ ] WebSockets (Socket.io) para atualizar o dashboard em tempo real
- [ ] Suporte a PowerShell alem de cmd.exe
- [ ] Upload/download de arquivos
- [ ] Persistencia via Task Scheduler / Registry / Startup Folder
- [ ] HTTPS com certificado self-signed
- [ ] Autenticacao JWT no dashboard
- [ ] Jitter configuravel via tasking do servidor

## Aviso Legal

Este projeto foi criado exclusivamente para fins educacionais e de pesquisa em seguranca ofensiva. Deve ser utilizado apenas em ambientes controlados e com autorizacao explicita (lab pessoal, VMs isoladas, ambientes de teste).

O autor nao se responsabiliza por qualquer uso indevido ou ilegal deste software. O uso contra sistemas sem autorizacao e crime previsto em lei.
