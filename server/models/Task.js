const mongoose = require('mongoose');

const taskSchema = new mongoose.Schema({
  agentId: {
    type:     mongoose.Schema.Types.ObjectId,
    ref:      'Agent',
    required: true
  },
  command:     { type: String, required: true },
  stdin:       { type: String, default: '' },
  shell: {
    type:    String,
    enum:    ['auto', 'cmd', 'powershell', 'bash'],
    default: 'auto'
  },
  status: {
    type:    String,
    enum:    ['pending', 'sent', 'completed', 'failed'],
    default: 'pending'
  },
  output:      { type: String, default: '' },
  createdAt:   { type: Date, default: Date.now },
  completedAt: { type: Date }
});

module.exports = mongoose.model('Task', taskSchema);