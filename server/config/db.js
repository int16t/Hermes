const mongoose = require('mongoose');

const connectDB = async () => {
  try {
    await mongoose.connect(process.env.MONGODB_URI);
    console.log('[+] MongoDB Atlas conectado');
  } catch (err) {
    console.error('[-] Erro ao conectar MongoDB:', err.message);
    process.exit(1);
  }
};

module.exports = connectDB;