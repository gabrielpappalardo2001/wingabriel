// Prova di calcolo per creare un account (la stessa di giochi/giochi.js: il server la chiede solo quando in un'ora si creano moltissimi account).
// Quando in un'ora si creano moltissimi account il server risponde 428 con una sfida: si cerca un numero (esadecimale minuscolo) tale che
// SHA-256 di "sfida|nome in minuscolo|numero" inizi con "bit" bit a zero. Servono pochi secondi per un account, troppi per migliaia.
// SHA-256 scritto qui: quello del browser è asincrono e per milioni di tentativi sarebbe troppo lento.
const K256 = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);
const W256 = new Uint32Array(64);
const M256 = new Uint8Array(256);   // il messaggio da calcolare, con il riempimento (basta fino a 246 byte: la prova ne usa circa 160)
// SHA-256 di bytes[0..lunghezza): scrive l'impronta in `fuori` (8 numeri a 32 bit)
function sha256Blocchi(bytes, lunghezza, fuori) {
  let h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a, h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
  const totale = ((lunghezza + 9 + 63) >> 6) << 6;
  if (totale > M256.length) throw new Error('Messaggio troppo lungo per la prova.');
  const m = M256;
  m.fill(0, 0, totale);
  m.set(bytes.subarray(0, lunghezza));
  m[lunghezza] = 0x80;
  const bit = lunghezza * 8;
  m[totale - 4] = (bit >>> 24) & 255; m[totale - 3] = (bit >>> 16) & 255; m[totale - 2] = (bit >>> 8) & 255; m[totale - 1] = bit & 255;
  for (let o = 0; o < totale; o += 64) {
    for (let i = 0; i < 16; i++) W256[i] = (m[o + 4 * i] << 24) | (m[o + 4 * i + 1] << 16) | (m[o + 4 * i + 2] << 8) | m[o + 4 * i + 3];
    for (let i = 16; i < 64; i++) {
      const a = W256[i - 15], b = W256[i - 2];
      const s0 = ((a >>> 7) | (a << 25)) ^ ((a >>> 18) | (a << 14)) ^ (a >>> 3);
      const s1 = ((b >>> 17) | (b << 15)) ^ ((b >>> 19) | (b << 13)) ^ (b >>> 10);
      W256[i] = (W256[i - 16] + s0 + W256[i - 7] + s1) | 0;
    }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (let i = 0; i < 64; i++) {
      const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
      const t1 = (h + S1 + ((e & f) ^ (~e & g)) + K256[i] + W256[i]) | 0;
      const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
      const t2 = (S0 + ((a & b) ^ (a & c) ^ (b & c))) | 0;
      h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
    }
    h0 = (h0 + a) | 0; h1 = (h1 + b) | 0; h2 = (h2 + c) | 0; h3 = (h3 + d) | 0; h4 = (h4 + e) | 0; h5 = (h5 + f) | 0; h6 = (h6 + g) | 0; h7 = (h7 + h) | 0;
  }
  fuori[0] = h0; fuori[1] = h1; fuori[2] = h2; fuori[3] = h3; fuori[4] = h4; fuori[5] = h5; fuori[6] = h6; fuori[7] = h7;
}
function zeriInizialiNumero(n) { return n === 0 ? 32 : Math.clz32(n); }
// Cerca il numero della prova. Lavora a pezzi di circa 80 ms per non bloccare la pagina e dice come procede con `avanza`.
export async function risolviProva(sfida, utente, bit, avanza) {
  const te = new TextEncoder();
  const prefisso = te.encode(sfida + '|' + utente.toLowerCase() + '|');
  const buffer = new Uint8Array(prefisso.length + 16);
  buffer.set(prefisso);
  const fuori = new Int32Array(8);
  const attesi = Math.pow(2, bit);
  let contatore = 0;
  for (;;) {
    const fine = performance.now() + 80;
    while (performance.now() < fine) {
      for (let k = 0; k < 500; k++) {
        const esa = contatore.toString(16);
        let n = prefisso.length;
        for (let i = 0; i < esa.length; i++) buffer[n++] = esa.charCodeAt(i);
        sha256Blocchi(buffer, n, fuori);
        const z = fuori[0] === 0 ? 32 + zeriInizialiNumero(fuori[1]) : zeriInizialiNumero(fuori[0]);
        if (z >= bit) return esa;
        contatore++;
      }
    }
    if (avanza) avanza(Math.min(99, Math.round((contatore / attesi) * 100)));
    await new Promise((ok) => setTimeout(ok, 0));
  }
}
