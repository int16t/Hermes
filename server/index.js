require('dotenv').config();

const express = require('express');
const cors    = require('cors');
const http   = require('http');

const https = require('https');
const fs     = require('fs');
const path   = require('path');

const connectDB    = require('./config/db');
const agentRoutes  = require('./routes/agents');
const taskRoutes   = require('./routes/tasks');
const { Server } = require('socket.io');

const app = express();

let server;

if (process.env.USE_HTTPS === 'true') {
  const keyPath  = process.env.TLS_KEY_PATH  || path.join(__dirname, '..', 'certs', 'server.key');
  const certPath = process.env.TLS_CERT_PATH || path.join(__dirname, '..', 'certs', 'server.cert');

  const options = {
    key:  fs.readFileSync(keyPath),
    cert: fs.readFileSync(certPath)
  };

  server = https.createServer(options, app);
} else {
  server = http.createServer(app);
}


const io = new Server(server);

app.use(cors());
app.use(express.json({ limit: '100mb' }));

app.use(express.static('client'));

app.use('/api/agents', agentRoutes(io));
app.use('/api/tasks',  taskRoutes);

app.get('/health', (req, res) => {
  res.json({ status: 'ok', time: new Date() });
});

const PORT = process.env.PORT || 3000;

connectDB().then(() => {
  server.listen(PORT, () => {
    const protocol = process.env.USE_HTTPS === 'true' ? 'https' : 'http';
    console.log(`[+] C2 Server rodando em ${protocol}://localhost:${PORT}`);
  });
});