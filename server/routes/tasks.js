const express = require('express');
const router  = express.Router();
const Task    = require('../models/Task');
const auth    = require('../middleware/auth');
const agentAuth = require('../middleware/agentAuth');

// POST /api/tasks — operador cria tarefa via dashboard (protegido)
router.post('/', auth, async (req, res) => {
  try {
    const { agentId, command } = req.body;
    const task = new Task({ agentId, command });
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

    task.output      = req.body.output;
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