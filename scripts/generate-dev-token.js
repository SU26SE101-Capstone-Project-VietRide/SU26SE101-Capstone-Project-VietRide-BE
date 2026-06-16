const jose = require('jose');

async function generate() {
  const privateKeyPem = `-----BEGIN PRIVATE KEY-----
MIIEuwIBADANBgkqhkiG9w0BAQEFAASCBKUwggShAgEAAoIBAQC+6Nk4TLBS4Hm3
p3/urqAAa+/eC1o+W4sbvmKEv2mZb9kxnTWwGudixb3bIxTD/5b468eI3cBftXZB
NMkgUBIeqC2KwYXdLE5uiDuhRTBNo21cY9mWRA9UocYiW8zEegoPevj9sbIvWATG
hvLVwkqi4j0UZhEwG7fmXKeJuGZfFGUXjnfKscNTVnV6hxcvtz9Txa9IgZdJyICr
Tk+MGh+qkrnt6iK3gx6NYufY9S+6ZkV0qA9tmLBVWMXAUg/VnNhRcfUbRM1HQLmG
HnC6w1ttMzsc8sbOI8Xt3/EXQDQaJjJfWgvaa1CnQx/AJz9co/qansxHFP37GueL
DAl0Th6BAgMBAAECgf9R7bLtx8z7Cf2PaqQrBIAaOdsITKhiMbiM3gSYIhjGtLle
EWsEkUFeitspmMGiFaU0ucxQh5QS8zXYUZS5Dxgr+KOSxcAtB8r+GNYJ9vjPcBkV
9fJ2le1EKoYAIXycGtrZVYQoct+zt3sPWsNQVhzgPnz38qb5T9SntkowCnLNK7R7
REXlJcRs2pOePukpKmFJwttZEmIkMv1zk1xmj0uAmM/SRr/Vir11d9uvz5UcRGYL
N+ig7qyQw7NSyv71EubDvvPcunM88whZ9oyTCQE7JcQBpq9QQ5+PBtEKLFv9Qddh
D9YT/Ys52hI92zhQNoi9+UTUpRlU6K8To2ZXr2cCgYEA+ASMtmTZU0giIi3zAl2P
5ysLwIXZzTmgPZXttCTfDNpqxaEY0tGLEP06JsPx5Us/bhLRmpQDgXwgMe8J6fi9
jk+n6rfoeTed3VhOAGGcABThzGU325JiCdIMpPMlrGsltisrgj8WJEdZYhtqx8W/
sCg/GWIe3+Qz7ceJSaYtXf8CgYEAxQ3G6IcTlZSdEosqTy/RWdsUDCmqo8EOpEgQ
cReN+Vq8JAwAz0UlABwee6na8dwRAGN4uaDdPf/q9NgTZhm3sBArFV0B/sVOIbNi
hH21136ER7MNTMJIm5TbNs2X9VoaZ94xAFSqrncj1PBsYL1jvvQaZ2h/p7q8kqJ0
nHlNg38CgYEAuWPNOtmPia01to7aQz5kvstycWqcL8ePe/mCQVH+WME7ZpbQ02VG
qmBfA3McceUZeNIgU4eoRzXdavXfV0FTj/kC73ShFVr5aecEB0zvKzBwyDQw2LRH
DEgyo2oNEyDUg6MpVqaJiny615be7o1mh+rNn8+0fG88UdUBTkglSUkCgYArfukC
9p3qDI3HRBSougNZ9DOuo5vY3Ypf1NBcRji+a7rPsh6Toc2TAqHv5gRAErVmAo7p
Woq7Xrv8I53UkaSsJkV8R7VjCSY/5hq+6Ai1cmW8dddftBrWzLq+lA8QxzzA5Jio
XAf4zq+IFzG1ANj9k2AopzZWTa/GJjnbOCNV/QKBgGm41nw1NY1mHvxTZOPwxvqL
ahIYVtjVZqKLkFhpfQ8rDziyjJMenOJrN1bNVFr5rp8qJooYgA7U8PUqFpznyMOy
na8Mnqco+K0o4Y25lSbqJBiE4uob0HBEHuRxtAKJGaT3S6uBQVrshFAnrUE9YttEh
3G9IZTcD9Xf6wKkFCbum
-----END PRIVATE KEY-----`;

  const kid = "dev-2026-05";
  const privateKey = await jose.importPKCS8(privateKeyPem, 'RS256');

  const jwt = await new jose.SignJWT({
    "role": "PASSENGER",
    "name": "Test Passenger",
    "email": "test@vietride.app",
    "hasPhone": true
  })
    .setProtectedHeader({ alg: 'RS256', kid: kid })
    .setSubject('00000000-0000-0000-0000-000000000001')
    .setIssuedAt()
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setExpirationTime('1d')
    .sign(privateKey);

  console.log(jwt);
}

generate().catch(console.error);
