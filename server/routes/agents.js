const express = require('express');
const router  = express.Router();
const Agent   = require('../models/Agent');
const auth    = require('../middleware/auth');

// POST /api/agents/register — agent C# faz check-in
router.post('/register', async (req, res) => {
  try {
    let { hostname, username, os, ip, arch, pid, token } = req.body;

    let agent = token 
    ? await Agent.findOne({ token }) 
    : await Agent.findOne({ hostname, ip });

    if (agent) {
      agent.lastSeen = new Date();
      agent.status   = 'active';
      if (!agent.token) {
        agent.token = require('crypto').randomUUID();
      }
      await agent.save();
      return res.json({ message: 'Check-in atualizado', agent });
    }
    
    token = require('crypto').randomUUID();
    agent = new Agent({ hostname, username, os, ip, arch, pid, token });
    await agent.save();
    res.status(201).json({ message: 'Agente registrado', agent });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// GET /api/agents — listar todos (protegido — só o dashboard acessa)
router.get('/', auth, async (req, res) => {
  try {
    const agents = await Agent.find().sort({ lastSeen: -1 });
    res.json(agents);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// GET /api/agents/:id — detalhes de um agente (protegido)
router.get('/:id', auth, async (req, res) => {
  try {
    const agent = await Agent.findById(req.params.id);
    if (!agent) return res.status(404).json({ error: 'Agente não encontrado' });
    res.json(agent);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// DELETE /api/agents/:id — remover agente (protegido)
router.delete('/:id', auth, async (req, res) => {
  try {
    await Agent.findByIdAndDelete(req.params.id);
    res.json({ message: 'Agente removido' });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

module.exports = router;