const mongoose = require('mongoose');

const agentSchema = new mongoose.Schema({
  hostname:     { type: String, required: true },
  username:     { type: String, required: true },
  os:           { type: String, required: true },
  ip:           { type: String, required: true },
  arch:         { type: String, default: 'x64' },
  pid:          { type: Number },
  token:        { type: String, unique: true },
  status: {
    type:    String,
    enum:    ['active', 'dormant', 'dead'],
    default: 'active'
  },
  lastSeen:     { type: Date, default: Date.now },
  registeredAt: { type: Date, default: Date.now }
});

module.exports = mongoose.model('Agent', agentSchema);