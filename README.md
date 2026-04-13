# C2 Dashboard Educacional

Projeto educacional para estudo de fluxo de tasking entre operador, API e agente.

## Sobre

- Stack do servidor: Node.js + Express + MongoDB
- Agente: C# (.NET)
- Frontend: HTML/CSS/JavaScript
- Uso: somente em ambiente controlado e autorizado

## Pré-requisitos

- Node.js 18+
- .NET SDK 8+
- MongoDB local ou Atlas

Validação rápida:

```bash
node --version
npm --version
dotnet --version
```

## Estrutura atual

```text
.
├── agent-csharp/
│   ├── agent-csharp.csproj
│   └── Program.cs
├── client/
│   ├── css/style.css
│   ├── js/app.js
│   └── index.html
├── server/
│   ├── config/db.js
│   ├── middleware/auth.js
│   ├── models/Agent.js
│   ├── models/Task.js
│   ├── routes/agents.js
│   ├── routes/tasks.js
│   └── index.js
├── .env
├── .env.example
├── .gitignore
├── package.json
└── README.md
```

## Configuracao

1. Instale dependencias do Node:

```bash
npm install
```

2. Crie o arquivo de ambiente a partir do exemplo:

```bash
cp .env.example .env
```

3. Edite o .env com os valores reais:

```env
PORT=3000
MONGODB_URI=mongodb://localhost:27017/c2dashboard
API_KEY=troque-esta-chave
```

## Executar projeto

Terminal 1 (API):

```bash
npm run dev
```

Terminal 2 (agente C#):

```bash
cd agent-csharp
dotnet build
dotnet run
```

Dashboard:

- http://localhost:3000

## Endpoints principais

Agentes:

- POST /api/agents/register
- GET /api/agents
- GET /api/agents/:id
- DELETE /api/agents/:id

Tarefas:

- POST /api/tasks
- GET /api/tasks/agent/:agentId
- PUT /api/tasks/:id/result
- GET /api/tasks/history/:agentId

## Aviso

Uso exclusivamente educacional.
