require('dotenv').config();

const express = require('express');
const cors    = require('cors');
const http   = require('http');

const connectDB    = require('./config/db');
const agentRoutes  = require('./routes/agents');
const taskRoutes   = require('./routes/tasks');
const { Server } = require('socket.io');

const app = express();

const server = http.createServer(app);  
const io = new Server(server);

app.use(cors());
app.use(express.json());

app.use(express.static('client'));

app.use('/api/agents', agentRoutes(io));
app.use('/api/tasks',  taskRoutes);

app.get('/health', (req, res) => {
  res.json({ status: 'ok', time: new Date() });
});

const PORT = process.env.PORT || 3000;

connectDB().then(() => {
  server.listen(PORT, () => {
    console.log(`[+] C2 Server rodando em http://localhost:${PORT}`);
  });
});