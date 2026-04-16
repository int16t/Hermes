const crypto = require('crypto');

const CURVE = 'prime256v1';
const HKDF_HASH = 'sha256';
const HKDF_INFO = 'hermes-bird-session';
const KEY_LENGTH = 32;

function generateECDHKeyPair() {
  const ecdh = crypto.createECDH(CURVE);
  ecdh.generateKeys();
  return {
    privateKey: ecdh.getPrivateKey('hex'),
    publicKey:  ecdh.getPublicKey('hex') 
  };
}

function deriveSessionKey(serverPrivHex, agentPubHex) {
  const ecdh = crypto.createECDH(CURVE);
  ecdh.setPrivateKey(serverPrivHex, 'hex');

  const sharedSecret = ecdh.computeSecret(agentPubHex, 'hex');

  const derived = crypto.hkdfSync(
    HKDF_HASH,
    sharedSecret,
    Buffer.alloc(0),                        
    Buffer.from(HKDF_INFO, 'utf8'),         
    KEY_LENGTH
  );

  return Buffer.from(derived).toString('hex');
}


function encrypt(plaintext, sessionKeyHex) {
  const key = Buffer.from(sessionKeyHex, 'hex');
  const iv  = crypto.randomBytes(12);

  const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
  const encrypted = Buffer.concat([
    cipher.update(plaintext, 'utf8'),
    cipher.final()
  ]);
  const tag = cipher.getAuthTag(); // 16 bytes

  return Buffer.concat([iv, encrypted, tag]).toString('base64');
}

function decrypt(encryptedBase64, sessionKeyHex) {
  const key = Buffer.from(sessionKeyHex, 'hex');
  const buf = Buffer.from(encryptedBase64, 'base64');

  const iv         = buf.subarray(0, 12);
  const tag        = buf.subarray(buf.length - 16);
  const ciphertext = buf.subarray(12, buf.length - 16);

  const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
  decipher.setAuthTag(tag);

  const decrypted = Buffer.concat([
    decipher.update(ciphertext),
    decipher.final()
  ]);

  return decrypted.toString('utf8');
}

module.exports = { generateECDHKeyPair, deriveSessionKey, encrypt, decrypt };
