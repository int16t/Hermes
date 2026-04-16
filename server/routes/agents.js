const express = require('express');
const router  = express.Router();
const Agent   = require('../models/Agent');
const auth    = require('../middleware/auth');
const { generateECDHKeyPair, deriveSessionKey } = require('../utils/crypto');


module.exports = function(io) {

router.post('/register', async (req, res) => {
  try {

    let { hostname, username, os, ip, arch, pid, token, cwd } = req.body;

    const { secret } = req.body;
    if (!token && secret !== process.env.AGENT_SECRET) {
    return res.status(403).json({ error: 'Secret invalido' });
  }

    let agent = token
    ? await Agent.findOne({ token })
    : await Agent.findOne({ hostname, ip });

    if (agent) {
      agent.lastSeen = new Date();
      agent.status   = 'active';
      if (cwd) agent.cwd = cwd;
      
      agent.token = require('crypto').randomUUID();
      
      let serverPublicKey = '';
      if (req.body.publicKey) {
        const serverKeys = generateECDHKeyPair();
        agent.sessionKey = deriveSessionKey(serverKeys.privateKey, req.body.publicKey);
        serverPublicKey = serverKeys.publicKey;
      }

      await agent.save();
      return res.json({ message: 'Check-in atualizado', agent, serverPublicKey});
    }

    token = require('crypto').randomUUID();

    let sessionKey = '';
    let serverPublicKey = '';

    if (req.body.publicKey) {
      const serverKeys = generateECDHKeyPair();
      sessionKey = deriveSessionKey(serverKeys.privateKey, req.body.publicKey);
      serverPublicKey = serverKeys.publicKey;
    }
    
    agent = new Agent({ hostname, username, os, ip, arch, pid, token, cwd, sessionKey });
    await agent.save();
    io.emit('agent-checkin', agent);
    res.status(201).json({ message: 'Agente registrado', agent, serverPublicKey });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

router.get('/', auth, async (req, res) => {
  try {
    const agents = await Agent.find().sort({ lastSeen: -1 });
    res.json(agents);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

router.get('/:id', auth, async (req, res) => {
  try {
    const agent = await Agent.findById(req.params.id);
    if (!agent) return res.status(404).json({ error: 'Agente não encontrado' });
    res.json(agent);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

router.delete('/:id', auth, async (req, res) => {
  try {
    await Agent.findByIdAndDelete(req.params.id);
    res.json({ message: 'Agente removido' });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

return router;
};