const Agent = require('../models/Agent');
const agentAuth = async (req, res, next) => {
  
    const agentToken = req.headers['x-api-token'];

    if (!agentToken) {
      return res.status(401).json({ error: 'Token não fornecido' });
    }

    try {
        const agent = await Agent.findOne({ token: agentToken });

        if (!agent) {
          return res.status(401).json({ error: 'Token inválido' });
        }
        req.agent = agent;
        next();
    }
    catch(err){
        return res.status(500).json({ error: 'Erro interno' });
    }
};

module.exports = agentAuth;