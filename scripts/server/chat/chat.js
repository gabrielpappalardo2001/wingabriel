// node_modules/@noble/hashes/esm/crypto.js
var crypto2 = typeof globalThis === "object" && "crypto" in globalThis ? globalThis.crypto : void 0;

// node_modules/@noble/hashes/esm/utils.js
/*! noble-hashes - MIT License (c) 2022 Paul Miller (paulmillr.com) */
function isBytes(a) {
  return a instanceof Uint8Array || ArrayBuffer.isView(a) && a.constructor.name === "Uint8Array";
}
function anumber(n) {
  if (!Number.isSafeInteger(n) || n < 0)
    throw new Error("positive integer expected, got " + n);
}
function abytes(b, ...lengths) {
  if (!isBytes(b))
    throw new Error("Uint8Array expected");
  if (lengths.length > 0 && !lengths.includes(b.length))
    throw new Error("Uint8Array expected of length " + lengths + ", got length=" + b.length);
}
function ahash(h) {
  if (typeof h !== "function" || typeof h.create !== "function")
    throw new Error("Hash should be wrapped by utils.createHasher");
  anumber(h.outputLen);
  anumber(h.blockLen);
}
function aexists(instance, checkFinished = true) {
  if (instance.destroyed)
    throw new Error("Hash instance has been destroyed");
  if (checkFinished && instance.finished)
    throw new Error("Hash#digest() has already been called");
}
function aoutput(out, instance) {
  abytes(out);
  const min = instance.outputLen;
  if (out.length < min) {
    throw new Error("digestInto() expects output buffer of length at least " + min);
  }
}
function u32(arr) {
  return new Uint32Array(arr.buffer, arr.byteOffset, Math.floor(arr.byteLength / 4));
}
function clean(...arrays) {
  for (let i = 0; i < arrays.length; i++) {
    arrays[i].fill(0);
  }
}
function createView(arr) {
  return new DataView(arr.buffer, arr.byteOffset, arr.byteLength);
}
function rotr(word, shift) {
  return word << 32 - shift | word >>> shift;
}
var isLE = /* @__PURE__ */ (() => new Uint8Array(new Uint32Array([287454020]).buffer)[0] === 68)();
function byteSwap(word) {
  return word << 24 & 4278190080 | word << 8 & 16711680 | word >>> 8 & 65280 | word >>> 24 & 255;
}
function byteSwap32(arr) {
  for (let i = 0; i < arr.length; i++) {
    arr[i] = byteSwap(arr[i]);
  }
  return arr;
}
var swap32IfBE = isLE ? (u) => u : byteSwap32;
var hasHexBuiltin = /* @__PURE__ */ (() => (
  // @ts-ignore
  typeof Uint8Array.from([]).toHex === "function" && typeof Uint8Array.fromHex === "function"
))();
var hexes = /* @__PURE__ */ Array.from({ length: 256 }, (_, i) => i.toString(16).padStart(2, "0"));
function bytesToHex(bytes) {
  abytes(bytes);
  if (hasHexBuiltin)
    return bytes.toHex();
  let hex = "";
  for (let i = 0; i < bytes.length; i++) {
    hex += hexes[bytes[i]];
  }
  return hex;
}
var asciis = { _0: 48, _9: 57, A: 65, F: 70, a: 97, f: 102 };
function asciiToBase16(ch) {
  if (ch >= asciis._0 && ch <= asciis._9)
    return ch - asciis._0;
  if (ch >= asciis.A && ch <= asciis.F)
    return ch - (asciis.A - 10);
  if (ch >= asciis.a && ch <= asciis.f)
    return ch - (asciis.a - 10);
  return;
}
function hexToBytes(hex) {
  if (typeof hex !== "string")
    throw new Error("hex string expected, got " + typeof hex);
  if (hasHexBuiltin)
    return Uint8Array.fromHex(hex);
  const hl = hex.length;
  const al = hl / 2;
  if (hl % 2)
    throw new Error("hex string expected, got unpadded hex of length " + hl);
  const array = new Uint8Array(al);
  for (let ai = 0, hi = 0; ai < al; ai++, hi += 2) {
    const n1 = asciiToBase16(hex.charCodeAt(hi));
    const n2 = asciiToBase16(hex.charCodeAt(hi + 1));
    if (n1 === void 0 || n2 === void 0) {
      const char = hex[hi] + hex[hi + 1];
      throw new Error('hex string expected, got non-hex character "' + char + '" at index ' + hi);
    }
    array[ai] = n1 * 16 + n2;
  }
  return array;
}
function utf8ToBytes(str) {
  if (typeof str !== "string")
    throw new Error("string expected");
  return new Uint8Array(new TextEncoder().encode(str));
}
function toBytes(data) {
  if (typeof data === "string")
    data = utf8ToBytes(data);
  abytes(data);
  return data;
}
function concatBytes(...arrays) {
  let sum = 0;
  for (let i = 0; i < arrays.length; i++) {
    const a = arrays[i];
    abytes(a);
    sum += a.length;
  }
  const res = new Uint8Array(sum);
  for (let i = 0, pad = 0; i < arrays.length; i++) {
    const a = arrays[i];
    res.set(a, pad);
    pad += a.length;
  }
  return res;
}
var Hash = class {
};
function createHasher(hashCons) {
  const hashC = (msg) => hashCons().update(toBytes(msg)).digest();
  const tmp = hashCons();
  hashC.outputLen = tmp.outputLen;
  hashC.blockLen = tmp.blockLen;
  hashC.create = () => hashCons();
  return hashC;
}
function createXOFer(hashCons) {
  const hashC = (msg, opts2) => hashCons(opts2).update(toBytes(msg)).digest();
  const tmp = hashCons({});
  hashC.outputLen = tmp.outputLen;
  hashC.blockLen = tmp.blockLen;
  hashC.create = (opts2) => hashCons(opts2);
  return hashC;
}
function randomBytes(bytesLength = 32) {
  if (crypto2 && typeof crypto2.getRandomValues === "function") {
    return crypto2.getRandomValues(new Uint8Array(bytesLength));
  }
  if (crypto2 && typeof crypto2.randomBytes === "function") {
    return Uint8Array.from(crypto2.randomBytes(bytesLength));
  }
  throw new Error("crypto.getRandomValues must be defined");
}

// node_modules/@noble/hashes/esm/_md.js
function setBigUint64(view, byteOffset, value, isLE3) {
  if (typeof view.setBigUint64 === "function")
    return view.setBigUint64(byteOffset, value, isLE3);
  const _32n2 = BigInt(32);
  const _u32_max = BigInt(4294967295);
  const wh = Number(value >> _32n2 & _u32_max);
  const wl = Number(value & _u32_max);
  const h = isLE3 ? 4 : 0;
  const l = isLE3 ? 0 : 4;
  view.setUint32(byteOffset + h, wh, isLE3);
  view.setUint32(byteOffset + l, wl, isLE3);
}
function Chi(a, b, c) {
  return a & b ^ ~a & c;
}
function Maj(a, b, c) {
  return a & b ^ a & c ^ b & c;
}
var HashMD = class extends Hash {
  constructor(blockLen, outputLen, padOffset, isLE3) {
    super();
    this.finished = false;
    this.length = 0;
    this.pos = 0;
    this.destroyed = false;
    this.blockLen = blockLen;
    this.outputLen = outputLen;
    this.padOffset = padOffset;
    this.isLE = isLE3;
    this.buffer = new Uint8Array(blockLen);
    this.view = createView(this.buffer);
  }
  update(data) {
    aexists(this);
    data = toBytes(data);
    abytes(data);
    const { view, buffer, blockLen } = this;
    const len = data.length;
    for (let pos = 0; pos < len; ) {
      const take = Math.min(blockLen - this.pos, len - pos);
      if (take === blockLen) {
        const dataView = createView(data);
        for (; blockLen <= len - pos; pos += blockLen)
          this.process(dataView, pos);
        continue;
      }
      buffer.set(data.subarray(pos, pos + take), this.pos);
      this.pos += take;
      pos += take;
      if (this.pos === blockLen) {
        this.process(view, 0);
        this.pos = 0;
      }
    }
    this.length += data.length;
    this.roundClean();
    return this;
  }
  digestInto(out) {
    aexists(this);
    aoutput(out, this);
    this.finished = true;
    const { buffer, view, blockLen, isLE: isLE3 } = this;
    let { pos } = this;
    buffer[pos++] = 128;
    clean(this.buffer.subarray(pos));
    if (this.padOffset > blockLen - pos) {
      this.process(view, 0);
      pos = 0;
    }
    for (let i = pos; i < blockLen; i++)
      buffer[i] = 0;
    setBigUint64(view, blockLen - 8, BigInt(this.length * 8), isLE3);
    this.process(view, 0);
    const oview = createView(out);
    const len = this.outputLen;
    if (len % 4)
      throw new Error("_sha2: outputLen should be aligned to 32bit");
    const outLen = len / 4;
    const state = this.get();
    if (outLen > state.length)
      throw new Error("_sha2: outputLen bigger than state");
    for (let i = 0; i < outLen; i++)
      oview.setUint32(4 * i, state[i], isLE3);
  }
  digest() {
    const { buffer, outputLen } = this;
    this.digestInto(buffer);
    const res = buffer.slice(0, outputLen);
    this.destroy();
    return res;
  }
  _cloneInto(to) {
    to || (to = new this.constructor());
    to.set(...this.get());
    const { blockLen, buffer, length, finished, destroyed, pos } = this;
    to.destroyed = destroyed;
    to.finished = finished;
    to.length = length;
    to.pos = pos;
    if (length % blockLen)
      to.buffer.set(buffer);
    return to;
  }
  clone() {
    return this._cloneInto();
  }
};
var SHA256_IV = /* @__PURE__ */ Uint32Array.from([
  1779033703,
  3144134277,
  1013904242,
  2773480762,
  1359893119,
  2600822924,
  528734635,
  1541459225
]);
var SHA224_IV = /* @__PURE__ */ Uint32Array.from([
  3238371032,
  914150663,
  812702999,
  4144912697,
  4290775857,
  1750603025,
  1694076839,
  3204075428
]);
var SHA384_IV = /* @__PURE__ */ Uint32Array.from([
  3418070365,
  3238371032,
  1654270250,
  914150663,
  2438529370,
  812702999,
  355462360,
  4144912697,
  1731405415,
  4290775857,
  2394180231,
  1750603025,
  3675008525,
  1694076839,
  1203062813,
  3204075428
]);
var SHA512_IV = /* @__PURE__ */ Uint32Array.from([
  1779033703,
  4089235720,
  3144134277,
  2227873595,
  1013904242,
  4271175723,
  2773480762,
  1595750129,
  1359893119,
  2917565137,
  2600822924,
  725511199,
  528734635,
  4215389547,
  1541459225,
  327033209
]);

// node_modules/@noble/hashes/esm/_u64.js
var U32_MASK64 = /* @__PURE__ */ BigInt(2 ** 32 - 1);
var _32n = /* @__PURE__ */ BigInt(32);
function fromBig(n, le = false) {
  if (le)
    return { h: Number(n & U32_MASK64), l: Number(n >> _32n & U32_MASK64) };
  return { h: Number(n >> _32n & U32_MASK64) | 0, l: Number(n & U32_MASK64) | 0 };
}
function split(lst, le = false) {
  const len = lst.length;
  let Ah = new Uint32Array(len);
  let Al = new Uint32Array(len);
  for (let i = 0; i < len; i++) {
    const { h, l } = fromBig(lst[i], le);
    [Ah[i], Al[i]] = [h, l];
  }
  return [Ah, Al];
}
var shrSH = (h, _l, s) => h >>> s;
var shrSL = (h, l, s) => h << 32 - s | l >>> s;
var rotrSH = (h, l, s) => h >>> s | l << 32 - s;
var rotrSL = (h, l, s) => h << 32 - s | l >>> s;
var rotrBH = (h, l, s) => h << 64 - s | l >>> s - 32;
var rotrBL = (h, l, s) => h >>> s - 32 | l << 64 - s;
var rotlSH = (h, l, s) => h << s | l >>> 32 - s;
var rotlSL = (h, l, s) => l << s | h >>> 32 - s;
var rotlBH = (h, l, s) => l << s - 32 | h >>> 64 - s;
var rotlBL = (h, l, s) => h << s - 32 | l >>> 64 - s;
function add(Ah, Al, Bh, Bl) {
  const l = (Al >>> 0) + (Bl >>> 0);
  return { h: Ah + Bh + (l / 2 ** 32 | 0) | 0, l: l | 0 };
}
var add3L = (Al, Bl, Cl) => (Al >>> 0) + (Bl >>> 0) + (Cl >>> 0);
var add3H = (low, Ah, Bh, Ch) => Ah + Bh + Ch + (low / 2 ** 32 | 0) | 0;
var add4L = (Al, Bl, Cl, Dl) => (Al >>> 0) + (Bl >>> 0) + (Cl >>> 0) + (Dl >>> 0);
var add4H = (low, Ah, Bh, Ch, Dh) => Ah + Bh + Ch + Dh + (low / 2 ** 32 | 0) | 0;
var add5L = (Al, Bl, Cl, Dl, El) => (Al >>> 0) + (Bl >>> 0) + (Cl >>> 0) + (Dl >>> 0) + (El >>> 0);
var add5H = (low, Ah, Bh, Ch, Dh, Eh) => Ah + Bh + Ch + Dh + Eh + (low / 2 ** 32 | 0) | 0;

// node_modules/@noble/hashes/esm/sha2.js
var SHA256_K = /* @__PURE__ */ Uint32Array.from([
  1116352408,
  1899447441,
  3049323471,
  3921009573,
  961987163,
  1508970993,
  2453635748,
  2870763221,
  3624381080,
  310598401,
  607225278,
  1426881987,
  1925078388,
  2162078206,
  2614888103,
  3248222580,
  3835390401,
  4022224774,
  264347078,
  604807628,
  770255983,
  1249150122,
  1555081692,
  1996064986,
  2554220882,
  2821834349,
  2952996808,
  3210313671,
  3336571891,
  3584528711,
  113926993,
  338241895,
  666307205,
  773529912,
  1294757372,
  1396182291,
  1695183700,
  1986661051,
  2177026350,
  2456956037,
  2730485921,
  2820302411,
  3259730800,
  3345764771,
  3516065817,
  3600352804,
  4094571909,
  275423344,
  430227734,
  506948616,
  659060556,
  883997877,
  958139571,
  1322822218,
  1537002063,
  1747873779,
  1955562222,
  2024104815,
  2227730452,
  2361852424,
  2428436474,
  2756734187,
  3204031479,
  3329325298
]);
var SHA256_W = /* @__PURE__ */ new Uint32Array(64);
var SHA256 = class extends HashMD {
  constructor(outputLen = 32) {
    super(64, outputLen, 8, false);
    this.A = SHA256_IV[0] | 0;
    this.B = SHA256_IV[1] | 0;
    this.C = SHA256_IV[2] | 0;
    this.D = SHA256_IV[3] | 0;
    this.E = SHA256_IV[4] | 0;
    this.F = SHA256_IV[5] | 0;
    this.G = SHA256_IV[6] | 0;
    this.H = SHA256_IV[7] | 0;
  }
  get() {
    const { A, B, C, D, E, F: F2, G, H } = this;
    return [A, B, C, D, E, F2, G, H];
  }
  // prettier-ignore
  set(A, B, C, D, E, F2, G, H) {
    this.A = A | 0;
    this.B = B | 0;
    this.C = C | 0;
    this.D = D | 0;
    this.E = E | 0;
    this.F = F2 | 0;
    this.G = G | 0;
    this.H = H | 0;
  }
  process(view, offset) {
    for (let i = 0; i < 16; i++, offset += 4)
      SHA256_W[i] = view.getUint32(offset, false);
    for (let i = 16; i < 64; i++) {
      const W15 = SHA256_W[i - 15];
      const W2 = SHA256_W[i - 2];
      const s0 = rotr(W15, 7) ^ rotr(W15, 18) ^ W15 >>> 3;
      const s1 = rotr(W2, 17) ^ rotr(W2, 19) ^ W2 >>> 10;
      SHA256_W[i] = s1 + SHA256_W[i - 7] + s0 + SHA256_W[i - 16] | 0;
    }
    let { A, B, C, D, E, F: F2, G, H } = this;
    for (let i = 0; i < 64; i++) {
      const sigma1 = rotr(E, 6) ^ rotr(E, 11) ^ rotr(E, 25);
      const T1 = H + sigma1 + Chi(E, F2, G) + SHA256_K[i] + SHA256_W[i] | 0;
      const sigma0 = rotr(A, 2) ^ rotr(A, 13) ^ rotr(A, 22);
      const T2 = sigma0 + Maj(A, B, C) | 0;
      H = G;
      G = F2;
      F2 = E;
      E = D + T1 | 0;
      D = C;
      C = B;
      B = A;
      A = T1 + T2 | 0;
    }
    A = A + this.A | 0;
    B = B + this.B | 0;
    C = C + this.C | 0;
    D = D + this.D | 0;
    E = E + this.E | 0;
    F2 = F2 + this.F | 0;
    G = G + this.G | 0;
    H = H + this.H | 0;
    this.set(A, B, C, D, E, F2, G, H);
  }
  roundClean() {
    clean(SHA256_W);
  }
  destroy() {
    this.set(0, 0, 0, 0, 0, 0, 0, 0);
    clean(this.buffer);
  }
};
var SHA224 = class extends SHA256 {
  constructor() {
    super(28);
    this.A = SHA224_IV[0] | 0;
    this.B = SHA224_IV[1] | 0;
    this.C = SHA224_IV[2] | 0;
    this.D = SHA224_IV[3] | 0;
    this.E = SHA224_IV[4] | 0;
    this.F = SHA224_IV[5] | 0;
    this.G = SHA224_IV[6] | 0;
    this.H = SHA224_IV[7] | 0;
  }
};
var K512 = /* @__PURE__ */ (() => split([
  "0x428a2f98d728ae22",
  "0x7137449123ef65cd",
  "0xb5c0fbcfec4d3b2f",
  "0xe9b5dba58189dbbc",
  "0x3956c25bf348b538",
  "0x59f111f1b605d019",
  "0x923f82a4af194f9b",
  "0xab1c5ed5da6d8118",
  "0xd807aa98a3030242",
  "0x12835b0145706fbe",
  "0x243185be4ee4b28c",
  "0x550c7dc3d5ffb4e2",
  "0x72be5d74f27b896f",
  "0x80deb1fe3b1696b1",
  "0x9bdc06a725c71235",
  "0xc19bf174cf692694",
  "0xe49b69c19ef14ad2",
  "0xefbe4786384f25e3",
  "0x0fc19dc68b8cd5b5",
  "0x240ca1cc77ac9c65",
  "0x2de92c6f592b0275",
  "0x4a7484aa6ea6e483",
  "0x5cb0a9dcbd41fbd4",
  "0x76f988da831153b5",
  "0x983e5152ee66dfab",
  "0xa831c66d2db43210",
  "0xb00327c898fb213f",
  "0xbf597fc7beef0ee4",
  "0xc6e00bf33da88fc2",
  "0xd5a79147930aa725",
  "0x06ca6351e003826f",
  "0x142929670a0e6e70",
  "0x27b70a8546d22ffc",
  "0x2e1b21385c26c926",
  "0x4d2c6dfc5ac42aed",
  "0x53380d139d95b3df",
  "0x650a73548baf63de",
  "0x766a0abb3c77b2a8",
  "0x81c2c92e47edaee6",
  "0x92722c851482353b",
  "0xa2bfe8a14cf10364",
  "0xa81a664bbc423001",
  "0xc24b8b70d0f89791",
  "0xc76c51a30654be30",
  "0xd192e819d6ef5218",
  "0xd69906245565a910",
  "0xf40e35855771202a",
  "0x106aa07032bbd1b8",
  "0x19a4c116b8d2d0c8",
  "0x1e376c085141ab53",
  "0x2748774cdf8eeb99",
  "0x34b0bcb5e19b48a8",
  "0x391c0cb3c5c95a63",
  "0x4ed8aa4ae3418acb",
  "0x5b9cca4f7763e373",
  "0x682e6ff3d6b2b8a3",
  "0x748f82ee5defb2fc",
  "0x78a5636f43172f60",
  "0x84c87814a1f0ab72",
  "0x8cc702081a6439ec",
  "0x90befffa23631e28",
  "0xa4506cebde82bde9",
  "0xbef9a3f7b2c67915",
  "0xc67178f2e372532b",
  "0xca273eceea26619c",
  "0xd186b8c721c0c207",
  "0xeada7dd6cde0eb1e",
  "0xf57d4f7fee6ed178",
  "0x06f067aa72176fba",
  "0x0a637dc5a2c898a6",
  "0x113f9804bef90dae",
  "0x1b710b35131c471b",
  "0x28db77f523047d84",
  "0x32caab7b40c72493",
  "0x3c9ebe0a15c9bebc",
  "0x431d67c49c100d4c",
  "0x4cc5d4becb3e42b6",
  "0x597f299cfc657e2a",
  "0x5fcb6fab3ad6faec",
  "0x6c44198c4a475817"
].map((n) => BigInt(n))))();
var SHA512_Kh = /* @__PURE__ */ (() => K512[0])();
var SHA512_Kl = /* @__PURE__ */ (() => K512[1])();
var SHA512_W_H = /* @__PURE__ */ new Uint32Array(80);
var SHA512_W_L = /* @__PURE__ */ new Uint32Array(80);
var SHA512 = class extends HashMD {
  constructor(outputLen = 64) {
    super(128, outputLen, 16, false);
    this.Ah = SHA512_IV[0] | 0;
    this.Al = SHA512_IV[1] | 0;
    this.Bh = SHA512_IV[2] | 0;
    this.Bl = SHA512_IV[3] | 0;
    this.Ch = SHA512_IV[4] | 0;
    this.Cl = SHA512_IV[5] | 0;
    this.Dh = SHA512_IV[6] | 0;
    this.Dl = SHA512_IV[7] | 0;
    this.Eh = SHA512_IV[8] | 0;
    this.El = SHA512_IV[9] | 0;
    this.Fh = SHA512_IV[10] | 0;
    this.Fl = SHA512_IV[11] | 0;
    this.Gh = SHA512_IV[12] | 0;
    this.Gl = SHA512_IV[13] | 0;
    this.Hh = SHA512_IV[14] | 0;
    this.Hl = SHA512_IV[15] | 0;
  }
  // prettier-ignore
  get() {
    const { Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl } = this;
    return [Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl];
  }
  // prettier-ignore
  set(Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl) {
    this.Ah = Ah | 0;
    this.Al = Al | 0;
    this.Bh = Bh | 0;
    this.Bl = Bl | 0;
    this.Ch = Ch | 0;
    this.Cl = Cl | 0;
    this.Dh = Dh | 0;
    this.Dl = Dl | 0;
    this.Eh = Eh | 0;
    this.El = El | 0;
    this.Fh = Fh | 0;
    this.Fl = Fl | 0;
    this.Gh = Gh | 0;
    this.Gl = Gl | 0;
    this.Hh = Hh | 0;
    this.Hl = Hl | 0;
  }
  process(view, offset) {
    for (let i = 0; i < 16; i++, offset += 4) {
      SHA512_W_H[i] = view.getUint32(offset);
      SHA512_W_L[i] = view.getUint32(offset += 4);
    }
    for (let i = 16; i < 80; i++) {
      const W15h = SHA512_W_H[i - 15] | 0;
      const W15l = SHA512_W_L[i - 15] | 0;
      const s0h = rotrSH(W15h, W15l, 1) ^ rotrSH(W15h, W15l, 8) ^ shrSH(W15h, W15l, 7);
      const s0l = rotrSL(W15h, W15l, 1) ^ rotrSL(W15h, W15l, 8) ^ shrSL(W15h, W15l, 7);
      const W2h = SHA512_W_H[i - 2] | 0;
      const W2l = SHA512_W_L[i - 2] | 0;
      const s1h = rotrSH(W2h, W2l, 19) ^ rotrBH(W2h, W2l, 61) ^ shrSH(W2h, W2l, 6);
      const s1l = rotrSL(W2h, W2l, 19) ^ rotrBL(W2h, W2l, 61) ^ shrSL(W2h, W2l, 6);
      const SUMl = add4L(s0l, s1l, SHA512_W_L[i - 7], SHA512_W_L[i - 16]);
      const SUMh = add4H(SUMl, s0h, s1h, SHA512_W_H[i - 7], SHA512_W_H[i - 16]);
      SHA512_W_H[i] = SUMh | 0;
      SHA512_W_L[i] = SUMl | 0;
    }
    let { Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl } = this;
    for (let i = 0; i < 80; i++) {
      const sigma1h = rotrSH(Eh, El, 14) ^ rotrSH(Eh, El, 18) ^ rotrBH(Eh, El, 41);
      const sigma1l = rotrSL(Eh, El, 14) ^ rotrSL(Eh, El, 18) ^ rotrBL(Eh, El, 41);
      const CHIh = Eh & Fh ^ ~Eh & Gh;
      const CHIl = El & Fl ^ ~El & Gl;
      const T1ll = add5L(Hl, sigma1l, CHIl, SHA512_Kl[i], SHA512_W_L[i]);
      const T1h = add5H(T1ll, Hh, sigma1h, CHIh, SHA512_Kh[i], SHA512_W_H[i]);
      const T1l = T1ll | 0;
      const sigma0h = rotrSH(Ah, Al, 28) ^ rotrBH(Ah, Al, 34) ^ rotrBH(Ah, Al, 39);
      const sigma0l = rotrSL(Ah, Al, 28) ^ rotrBL(Ah, Al, 34) ^ rotrBL(Ah, Al, 39);
      const MAJh = Ah & Bh ^ Ah & Ch ^ Bh & Ch;
      const MAJl = Al & Bl ^ Al & Cl ^ Bl & Cl;
      Hh = Gh | 0;
      Hl = Gl | 0;
      Gh = Fh | 0;
      Gl = Fl | 0;
      Fh = Eh | 0;
      Fl = El | 0;
      ({ h: Eh, l: El } = add(Dh | 0, Dl | 0, T1h | 0, T1l | 0));
      Dh = Ch | 0;
      Dl = Cl | 0;
      Ch = Bh | 0;
      Cl = Bl | 0;
      Bh = Ah | 0;
      Bl = Al | 0;
      const All = add3L(T1l, sigma0l, MAJl);
      Ah = add3H(All, T1h, sigma0h, MAJh);
      Al = All | 0;
    }
    ({ h: Ah, l: Al } = add(this.Ah | 0, this.Al | 0, Ah | 0, Al | 0));
    ({ h: Bh, l: Bl } = add(this.Bh | 0, this.Bl | 0, Bh | 0, Bl | 0));
    ({ h: Ch, l: Cl } = add(this.Ch | 0, this.Cl | 0, Ch | 0, Cl | 0));
    ({ h: Dh, l: Dl } = add(this.Dh | 0, this.Dl | 0, Dh | 0, Dl | 0));
    ({ h: Eh, l: El } = add(this.Eh | 0, this.El | 0, Eh | 0, El | 0));
    ({ h: Fh, l: Fl } = add(this.Fh | 0, this.Fl | 0, Fh | 0, Fl | 0));
    ({ h: Gh, l: Gl } = add(this.Gh | 0, this.Gl | 0, Gh | 0, Gl | 0));
    ({ h: Hh, l: Hl } = add(this.Hh | 0, this.Hl | 0, Hh | 0, Hl | 0));
    this.set(Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl);
  }
  roundClean() {
    clean(SHA512_W_H, SHA512_W_L);
  }
  destroy() {
    clean(this.buffer);
    this.set(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
  }
};
var SHA384 = class extends SHA512 {
  constructor() {
    super(48);
    this.Ah = SHA384_IV[0] | 0;
    this.Al = SHA384_IV[1] | 0;
    this.Bh = SHA384_IV[2] | 0;
    this.Bl = SHA384_IV[3] | 0;
    this.Ch = SHA384_IV[4] | 0;
    this.Cl = SHA384_IV[5] | 0;
    this.Dh = SHA384_IV[6] | 0;
    this.Dl = SHA384_IV[7] | 0;
    this.Eh = SHA384_IV[8] | 0;
    this.El = SHA384_IV[9] | 0;
    this.Fh = SHA384_IV[10] | 0;
    this.Fl = SHA384_IV[11] | 0;
    this.Gh = SHA384_IV[12] | 0;
    this.Gl = SHA384_IV[13] | 0;
    this.Hh = SHA384_IV[14] | 0;
    this.Hl = SHA384_IV[15] | 0;
  }
};
var T224_IV = /* @__PURE__ */ Uint32Array.from([
  2352822216,
  424955298,
  1944164710,
  2312950998,
  502970286,
  855612546,
  1738396948,
  1479516111,
  258812777,
  2077511080,
  2011393907,
  79989058,
  1067287976,
  1780299464,
  286451373,
  2446758561
]);
var T256_IV = /* @__PURE__ */ Uint32Array.from([
  573645204,
  4230739756,
  2673172387,
  3360449730,
  596883563,
  1867755857,
  2520282905,
  1497426621,
  2519219938,
  2827943907,
  3193839141,
  1401305490,
  721525244,
  746961066,
  246885852,
  2177182882
]);
var SHA512_224 = class extends SHA512 {
  constructor() {
    super(28);
    this.Ah = T224_IV[0] | 0;
    this.Al = T224_IV[1] | 0;
    this.Bh = T224_IV[2] | 0;
    this.Bl = T224_IV[3] | 0;
    this.Ch = T224_IV[4] | 0;
    this.Cl = T224_IV[5] | 0;
    this.Dh = T224_IV[6] | 0;
    this.Dl = T224_IV[7] | 0;
    this.Eh = T224_IV[8] | 0;
    this.El = T224_IV[9] | 0;
    this.Fh = T224_IV[10] | 0;
    this.Fl = T224_IV[11] | 0;
    this.Gh = T224_IV[12] | 0;
    this.Gl = T224_IV[13] | 0;
    this.Hh = T224_IV[14] | 0;
    this.Hl = T224_IV[15] | 0;
  }
};
var SHA512_256 = class extends SHA512 {
  constructor() {
    super(32);
    this.Ah = T256_IV[0] | 0;
    this.Al = T256_IV[1] | 0;
    this.Bh = T256_IV[2] | 0;
    this.Bl = T256_IV[3] | 0;
    this.Ch = T256_IV[4] | 0;
    this.Cl = T256_IV[5] | 0;
    this.Dh = T256_IV[6] | 0;
    this.Dl = T256_IV[7] | 0;
    this.Eh = T256_IV[8] | 0;
    this.El = T256_IV[9] | 0;
    this.Fh = T256_IV[10] | 0;
    this.Fl = T256_IV[11] | 0;
    this.Gh = T256_IV[12] | 0;
    this.Gl = T256_IV[13] | 0;
    this.Hh = T256_IV[14] | 0;
    this.Hl = T256_IV[15] | 0;
  }
};
var sha256 = /* @__PURE__ */ createHasher(() => new SHA256());
var sha224 = /* @__PURE__ */ createHasher(() => new SHA224());
var sha512 = /* @__PURE__ */ createHasher(() => new SHA512());
var sha384 = /* @__PURE__ */ createHasher(() => new SHA384());
var sha512_256 = /* @__PURE__ */ createHasher(() => new SHA512_256());
var sha512_224 = /* @__PURE__ */ createHasher(() => new SHA512_224());

// node_modules/@noble/curves/esm/utils.js
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
var _0n = /* @__PURE__ */ BigInt(0);
var _1n = /* @__PURE__ */ BigInt(1);
function _abool2(value, title = "") {
  if (typeof value !== "boolean") {
    const prefix = title && `"${title}"`;
    throw new Error(prefix + "expected boolean, got type=" + typeof value);
  }
  return value;
}
function _abytes2(value, length, title = "") {
  const bytes = isBytes(value);
  const len = value?.length;
  const needsLen = length !== void 0;
  if (!bytes || needsLen && len !== length) {
    const prefix = title && `"${title}" `;
    const ofLen = needsLen ? ` of length ${length}` : "";
    const got = bytes ? `length=${len}` : `type=${typeof value}`;
    throw new Error(prefix + "expected Uint8Array" + ofLen + ", got " + got);
  }
  return value;
}
function hexToNumber(hex) {
  if (typeof hex !== "string")
    throw new Error("hex string expected, got " + typeof hex);
  return hex === "" ? _0n : BigInt("0x" + hex);
}
function bytesToNumberBE(bytes) {
  return hexToNumber(bytesToHex(bytes));
}
function bytesToNumberLE(bytes) {
  abytes(bytes);
  return hexToNumber(bytesToHex(Uint8Array.from(bytes).reverse()));
}
function numberToBytesBE(n, len) {
  return hexToBytes(n.toString(16).padStart(len * 2, "0"));
}
function numberToBytesLE(n, len) {
  return numberToBytesBE(n, len).reverse();
}
function ensureBytes(title, hex, expectedLength) {
  let res;
  if (typeof hex === "string") {
    try {
      res = hexToBytes(hex);
    } catch (e) {
      throw new Error(title + " must be hex string or Uint8Array, cause: " + e);
    }
  } else if (isBytes(hex)) {
    res = Uint8Array.from(hex);
  } else {
    throw new Error(title + " must be hex string or Uint8Array");
  }
  const len = res.length;
  if (typeof expectedLength === "number" && len !== expectedLength)
    throw new Error(title + " of length " + expectedLength + " expected, got " + len);
  return res;
}
function equalBytes(a, b) {
  if (a.length !== b.length)
    return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++)
    diff |= a[i] ^ b[i];
  return diff === 0;
}
function copyBytes(bytes) {
  return Uint8Array.from(bytes);
}
var isPosBig = (n) => typeof n === "bigint" && _0n <= n;
function inRange(n, min, max) {
  return isPosBig(n) && isPosBig(min) && isPosBig(max) && min <= n && n < max;
}
function aInRange(title, n, min, max) {
  if (!inRange(n, min, max))
    throw new Error("expected valid " + title + ": " + min + " <= n < " + max + ", got " + n);
}
function bitLen(n) {
  let len;
  for (len = 0; n > _0n; n >>= _1n, len += 1)
    ;
  return len;
}
var bitMask = (n) => (_1n << BigInt(n)) - _1n;
function _validateObject(object, fields, optFields = {}) {
  if (!object || typeof object !== "object")
    throw new Error("expected valid options object");
  function checkField(fieldName, expectedType, isOpt) {
    const val = object[fieldName];
    if (isOpt && val === void 0)
      return;
    const current = typeof val;
    if (current !== expectedType || val === null)
      throw new Error(`param "${fieldName}" is invalid: expected ${expectedType}, got ${current}`);
  }
  Object.entries(fields).forEach(([k, v]) => checkField(k, v, false));
  Object.entries(optFields).forEach(([k, v]) => checkField(k, v, true));
}
var notImplemented = () => {
  throw new Error("not implemented");
};
function memoized(fn) {
  const map = /* @__PURE__ */ new WeakMap();
  return (arg, ...args) => {
    const val = map.get(arg);
    if (val !== void 0)
      return val;
    const computed = fn(arg, ...args);
    map.set(arg, computed);
    return computed;
  };
}

// node_modules/@noble/curves/esm/abstract/modular.js
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
var _0n2 = BigInt(0);
var _1n2 = BigInt(1);
var _2n = /* @__PURE__ */ BigInt(2);
var _3n = /* @__PURE__ */ BigInt(3);
var _4n = /* @__PURE__ */ BigInt(4);
var _5n = /* @__PURE__ */ BigInt(5);
var _7n = /* @__PURE__ */ BigInt(7);
var _8n = /* @__PURE__ */ BigInt(8);
var _9n = /* @__PURE__ */ BigInt(9);
var _16n = /* @__PURE__ */ BigInt(16);
function mod(a, b) {
  const result = a % b;
  return result >= _0n2 ? result : b + result;
}
function pow2(x, power, modulo) {
  let res = x;
  while (power-- > _0n2) {
    res *= res;
    res %= modulo;
  }
  return res;
}
function invert(number, modulo) {
  if (number === _0n2)
    throw new Error("invert: expected non-zero number");
  if (modulo <= _0n2)
    throw new Error("invert: expected positive modulus, got " + modulo);
  let a = mod(number, modulo);
  let b = modulo;
  let x = _0n2, y = _1n2, u = _1n2, v = _0n2;
  while (a !== _0n2) {
    const q = b / a;
    const r = b % a;
    const m = x - u * q;
    const n = y - v * q;
    b = a, a = r, x = u, y = v, u = m, v = n;
  }
  const gcd = b;
  if (gcd !== _1n2)
    throw new Error("invert: does not exist");
  return mod(x, modulo);
}
function assertIsSquare(Fp2, root, n) {
  if (!Fp2.eql(Fp2.sqr(root), n))
    throw new Error("Cannot find square root");
}
function sqrt3mod4(Fp2, n) {
  const p1div4 = (Fp2.ORDER + _1n2) / _4n;
  const root = Fp2.pow(n, p1div4);
  assertIsSquare(Fp2, root, n);
  return root;
}
function sqrt5mod8(Fp2, n) {
  const p5div8 = (Fp2.ORDER - _5n) / _8n;
  const n2 = Fp2.mul(n, _2n);
  const v = Fp2.pow(n2, p5div8);
  const nv = Fp2.mul(n, v);
  const i = Fp2.mul(Fp2.mul(nv, _2n), v);
  const root = Fp2.mul(nv, Fp2.sub(i, Fp2.ONE));
  assertIsSquare(Fp2, root, n);
  return root;
}
function sqrt9mod16(P) {
  const Fp_ = Field(P);
  const tn = tonelliShanks(P);
  const c1 = tn(Fp_, Fp_.neg(Fp_.ONE));
  const c2 = tn(Fp_, c1);
  const c3 = tn(Fp_, Fp_.neg(c1));
  const c4 = (P + _7n) / _16n;
  return (Fp2, n) => {
    let tv1 = Fp2.pow(n, c4);
    let tv2 = Fp2.mul(tv1, c1);
    const tv3 = Fp2.mul(tv1, c2);
    const tv4 = Fp2.mul(tv1, c3);
    const e1 = Fp2.eql(Fp2.sqr(tv2), n);
    const e2 = Fp2.eql(Fp2.sqr(tv3), n);
    tv1 = Fp2.cmov(tv1, tv2, e1);
    tv2 = Fp2.cmov(tv4, tv3, e2);
    const e3 = Fp2.eql(Fp2.sqr(tv2), n);
    const root = Fp2.cmov(tv1, tv2, e3);
    assertIsSquare(Fp2, root, n);
    return root;
  };
}
function tonelliShanks(P) {
  if (P < _3n)
    throw new Error("sqrt is not defined for small field");
  let Q2 = P - _1n2;
  let S = 0;
  while (Q2 % _2n === _0n2) {
    Q2 /= _2n;
    S++;
  }
  let Z = _2n;
  const _Fp = Field(P);
  while (FpLegendre(_Fp, Z) === 1) {
    if (Z++ > 1e3)
      throw new Error("Cannot find square root: probably non-prime P");
  }
  if (S === 1)
    return sqrt3mod4;
  let cc = _Fp.pow(Z, Q2);
  const Q1div2 = (Q2 + _1n2) / _2n;
  return function tonelliSlow(Fp2, n) {
    if (Fp2.is0(n))
      return n;
    if (FpLegendre(Fp2, n) !== 1)
      throw new Error("Cannot find square root");
    let M = S;
    let c = Fp2.mul(Fp2.ONE, cc);
    let t = Fp2.pow(n, Q2);
    let R = Fp2.pow(n, Q1div2);
    while (!Fp2.eql(t, Fp2.ONE)) {
      if (Fp2.is0(t))
        return Fp2.ZERO;
      let i = 1;
      let t_tmp = Fp2.sqr(t);
      while (!Fp2.eql(t_tmp, Fp2.ONE)) {
        i++;
        t_tmp = Fp2.sqr(t_tmp);
        if (i === M)
          throw new Error("Cannot find square root");
      }
      const exponent = _1n2 << BigInt(M - i - 1);
      const b = Fp2.pow(c, exponent);
      M = i;
      c = Fp2.sqr(b);
      t = Fp2.mul(t, c);
      R = Fp2.mul(R, b);
    }
    return R;
  };
}
function FpSqrt(P) {
  if (P % _4n === _3n)
    return sqrt3mod4;
  if (P % _8n === _5n)
    return sqrt5mod8;
  if (P % _16n === _9n)
    return sqrt9mod16(P);
  return tonelliShanks(P);
}
var isNegativeLE = (num, modulo) => (mod(num, modulo) & _1n2) === _1n2;
var FIELD_FIELDS = [
  "create",
  "isValid",
  "is0",
  "neg",
  "inv",
  "sqrt",
  "sqr",
  "eql",
  "add",
  "sub",
  "mul",
  "pow",
  "div",
  "addN",
  "subN",
  "mulN",
  "sqrN"
];
function validateField(field) {
  const initial = {
    ORDER: "bigint",
    MASK: "bigint",
    BYTES: "number",
    BITS: "number"
  };
  const opts2 = FIELD_FIELDS.reduce((map, val) => {
    map[val] = "function";
    return map;
  }, initial);
  _validateObject(field, opts2);
  return field;
}
function FpPow(Fp2, num, power) {
  if (power < _0n2)
    throw new Error("invalid exponent, negatives unsupported");
  if (power === _0n2)
    return Fp2.ONE;
  if (power === _1n2)
    return num;
  let p = Fp2.ONE;
  let d = num;
  while (power > _0n2) {
    if (power & _1n2)
      p = Fp2.mul(p, d);
    d = Fp2.sqr(d);
    power >>= _1n2;
  }
  return p;
}
function FpInvertBatch(Fp2, nums, passZero = false) {
  const inverted = new Array(nums.length).fill(passZero ? Fp2.ZERO : void 0);
  const multipliedAcc = nums.reduce((acc, num, i) => {
    if (Fp2.is0(num))
      return acc;
    inverted[i] = acc;
    return Fp2.mul(acc, num);
  }, Fp2.ONE);
  const invertedAcc = Fp2.inv(multipliedAcc);
  nums.reduceRight((acc, num, i) => {
    if (Fp2.is0(num))
      return acc;
    inverted[i] = Fp2.mul(acc, inverted[i]);
    return Fp2.mul(acc, num);
  }, invertedAcc);
  return inverted;
}
function FpLegendre(Fp2, n) {
  const p1mod2 = (Fp2.ORDER - _1n2) / _2n;
  const powered = Fp2.pow(n, p1mod2);
  const yes = Fp2.eql(powered, Fp2.ONE);
  const zero = Fp2.eql(powered, Fp2.ZERO);
  const no = Fp2.eql(powered, Fp2.neg(Fp2.ONE));
  if (!yes && !zero && !no)
    throw new Error("invalid Legendre symbol result");
  return yes ? 1 : zero ? 0 : -1;
}
function nLength(n, nBitLength) {
  if (nBitLength !== void 0)
    anumber(nBitLength);
  const _nBitLength = nBitLength !== void 0 ? nBitLength : n.toString(2).length;
  const nByteLength = Math.ceil(_nBitLength / 8);
  return { nBitLength: _nBitLength, nByteLength };
}
function Field(ORDER, bitLenOrOpts, isLE3 = false, opts2 = {}) {
  if (ORDER <= _0n2)
    throw new Error("invalid field: expected ORDER > 0, got " + ORDER);
  let _nbitLength = void 0;
  let _sqrt = void 0;
  let modFromBytes = false;
  let allowedLengths = void 0;
  if (typeof bitLenOrOpts === "object" && bitLenOrOpts != null) {
    if (opts2.sqrt || isLE3)
      throw new Error("cannot specify opts in two arguments");
    const _opts = bitLenOrOpts;
    if (_opts.BITS)
      _nbitLength = _opts.BITS;
    if (_opts.sqrt)
      _sqrt = _opts.sqrt;
    if (typeof _opts.isLE === "boolean")
      isLE3 = _opts.isLE;
    if (typeof _opts.modFromBytes === "boolean")
      modFromBytes = _opts.modFromBytes;
    allowedLengths = _opts.allowedLengths;
  } else {
    if (typeof bitLenOrOpts === "number")
      _nbitLength = bitLenOrOpts;
    if (opts2.sqrt)
      _sqrt = opts2.sqrt;
  }
  const { nBitLength: BITS, nByteLength: BYTES } = nLength(ORDER, _nbitLength);
  if (BYTES > 2048)
    throw new Error("invalid field: expected ORDER of <= 2048 bytes");
  let sqrtP;
  const f = Object.freeze({
    ORDER,
    isLE: isLE3,
    BITS,
    BYTES,
    MASK: bitMask(BITS),
    ZERO: _0n2,
    ONE: _1n2,
    allowedLengths,
    create: (num) => mod(num, ORDER),
    isValid: (num) => {
      if (typeof num !== "bigint")
        throw new Error("invalid field element: expected bigint, got " + typeof num);
      return _0n2 <= num && num < ORDER;
    },
    is0: (num) => num === _0n2,
    // is valid and invertible
    isValidNot0: (num) => !f.is0(num) && f.isValid(num),
    isOdd: (num) => (num & _1n2) === _1n2,
    neg: (num) => mod(-num, ORDER),
    eql: (lhs, rhs) => lhs === rhs,
    sqr: (num) => mod(num * num, ORDER),
    add: (lhs, rhs) => mod(lhs + rhs, ORDER),
    sub: (lhs, rhs) => mod(lhs - rhs, ORDER),
    mul: (lhs, rhs) => mod(lhs * rhs, ORDER),
    pow: (num, power) => FpPow(f, num, power),
    div: (lhs, rhs) => mod(lhs * invert(rhs, ORDER), ORDER),
    // Same as above, but doesn't normalize
    sqrN: (num) => num * num,
    addN: (lhs, rhs) => lhs + rhs,
    subN: (lhs, rhs) => lhs - rhs,
    mulN: (lhs, rhs) => lhs * rhs,
    inv: (num) => invert(num, ORDER),
    sqrt: _sqrt || ((n) => {
      if (!sqrtP)
        sqrtP = FpSqrt(ORDER);
      return sqrtP(f, n);
    }),
    toBytes: (num) => isLE3 ? numberToBytesLE(num, BYTES) : numberToBytesBE(num, BYTES),
    fromBytes: (bytes, skipValidation = true) => {
      if (allowedLengths) {
        if (!allowedLengths.includes(bytes.length) || bytes.length > BYTES) {
          throw new Error("Field.fromBytes: expected " + allowedLengths + " bytes, got " + bytes.length);
        }
        const padded = new Uint8Array(BYTES);
        padded.set(bytes, isLE3 ? 0 : padded.length - bytes.length);
        bytes = padded;
      }
      if (bytes.length !== BYTES)
        throw new Error("Field.fromBytes: expected " + BYTES + " bytes, got " + bytes.length);
      let scalar = isLE3 ? bytesToNumberLE(bytes) : bytesToNumberBE(bytes);
      if (modFromBytes)
        scalar = mod(scalar, ORDER);
      if (!skipValidation) {
        if (!f.isValid(scalar))
          throw new Error("invalid field element: outside of range 0..ORDER");
      }
      return scalar;
    },
    // TODO: we don't need it here, move out to separate fn
    invertBatch: (lst) => FpInvertBatch(f, lst),
    // We can't move this out because Fp6, Fp12 implement it
    // and it's unclear what to return in there.
    cmov: (a, b, c) => c ? b : a
  });
  return Object.freeze(f);
}

// node_modules/@noble/curves/esm/abstract/curve.js
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
var _0n3 = BigInt(0);
var _1n3 = BigInt(1);
function negateCt(condition, item) {
  const neg = item.negate();
  return condition ? neg : item;
}
function normalizeZ(c, points) {
  const invertedZs = FpInvertBatch(c.Fp, points.map((p) => p.Z));
  return points.map((p, i) => c.fromAffine(p.toAffine(invertedZs[i])));
}
function validateW(W, bits) {
  if (!Number.isSafeInteger(W) || W <= 0 || W > bits)
    throw new Error("invalid window size, expected [1.." + bits + "], got W=" + W);
}
function calcWOpts(W, scalarBits) {
  validateW(W, scalarBits);
  const windows = Math.ceil(scalarBits / W) + 1;
  const windowSize = 2 ** (W - 1);
  const maxNumber = 2 ** W;
  const mask = bitMask(W);
  const shiftBy = BigInt(W);
  return { windows, windowSize, mask, maxNumber, shiftBy };
}
function calcOffsets(n, window2, wOpts) {
  const { windowSize, mask, maxNumber, shiftBy } = wOpts;
  let wbits = Number(n & mask);
  let nextN = n >> shiftBy;
  if (wbits > windowSize) {
    wbits -= maxNumber;
    nextN += _1n3;
  }
  const offsetStart = window2 * windowSize;
  const offset = offsetStart + Math.abs(wbits) - 1;
  const isZero = wbits === 0;
  const isNeg = wbits < 0;
  const isNegF = window2 % 2 !== 0;
  const offsetF = offsetStart;
  return { nextN, offset, isZero, isNeg, isNegF, offsetF };
}
function validateMSMPoints(points, c) {
  if (!Array.isArray(points))
    throw new Error("array expected");
  points.forEach((p, i) => {
    if (!(p instanceof c))
      throw new Error("invalid point at index " + i);
  });
}
function validateMSMScalars(scalars, field) {
  if (!Array.isArray(scalars))
    throw new Error("array of scalars expected");
  scalars.forEach((s, i) => {
    if (!field.isValid(s))
      throw new Error("invalid scalar at index " + i);
  });
}
var pointPrecomputes = /* @__PURE__ */ new WeakMap();
var pointWindowSizes = /* @__PURE__ */ new WeakMap();
function getW(P) {
  return pointWindowSizes.get(P) || 1;
}
function assert0(n) {
  if (n !== _0n3)
    throw new Error("invalid wNAF");
}
var wNAF = class {
  // Parametrized with a given Point class (not individual point)
  constructor(Point, bits) {
    this.BASE = Point.BASE;
    this.ZERO = Point.ZERO;
    this.Fn = Point.Fn;
    this.bits = bits;
  }
  // non-const time multiplication ladder
  _unsafeLadder(elm, n, p = this.ZERO) {
    let d = elm;
    while (n > _0n3) {
      if (n & _1n3)
        p = p.add(d);
      d = d.double();
      n >>= _1n3;
    }
    return p;
  }
  /**
   * Creates a wNAF precomputation window. Used for caching.
   * Default window size is set by `utils.precompute()` and is equal to 8.
   * Number of precomputed points depends on the curve size:
   * 2^(𝑊−1) * (Math.ceil(𝑛 / 𝑊) + 1), where:
   * - 𝑊 is the window size
   * - 𝑛 is the bitlength of the curve order.
   * For a 256-bit curve and window size 8, the number of precomputed points is 128 * 33 = 4224.
   * @param point Point instance
   * @param W window size
   * @returns precomputed point tables flattened to a single array
   */
  precomputeWindow(point, W) {
    const { windows, windowSize } = calcWOpts(W, this.bits);
    const points = [];
    let p = point;
    let base = p;
    for (let window2 = 0; window2 < windows; window2++) {
      base = p;
      points.push(base);
      for (let i = 1; i < windowSize; i++) {
        base = base.add(p);
        points.push(base);
      }
      p = base.double();
    }
    return points;
  }
  /**
   * Implements ec multiplication using precomputed tables and w-ary non-adjacent form.
   * More compact implementation:
   * https://github.com/paulmillr/noble-secp256k1/blob/47cb1669b6e506ad66b35fe7d76132ae97465da2/index.ts#L502-L541
   * @returns real and fake (for const-time) points
   */
  wNAF(W, precomputes, n) {
    if (!this.Fn.isValid(n))
      throw new Error("invalid scalar");
    let p = this.ZERO;
    let f = this.BASE;
    const wo = calcWOpts(W, this.bits);
    for (let window2 = 0; window2 < wo.windows; window2++) {
      const { nextN, offset, isZero, isNeg, isNegF, offsetF } = calcOffsets(n, window2, wo);
      n = nextN;
      if (isZero) {
        f = f.add(negateCt(isNegF, precomputes[offsetF]));
      } else {
        p = p.add(negateCt(isNeg, precomputes[offset]));
      }
    }
    assert0(n);
    return { p, f };
  }
  /**
   * Implements ec unsafe (non const-time) multiplication using precomputed tables and w-ary non-adjacent form.
   * @param acc accumulator point to add result of multiplication
   * @returns point
   */
  wNAFUnsafe(W, precomputes, n, acc = this.ZERO) {
    const wo = calcWOpts(W, this.bits);
    for (let window2 = 0; window2 < wo.windows; window2++) {
      if (n === _0n3)
        break;
      const { nextN, offset, isZero, isNeg } = calcOffsets(n, window2, wo);
      n = nextN;
      if (isZero) {
        continue;
      } else {
        const item = precomputes[offset];
        acc = acc.add(isNeg ? item.negate() : item);
      }
    }
    assert0(n);
    return acc;
  }
  getPrecomputes(W, point, transform) {
    let comp = pointPrecomputes.get(point);
    if (!comp) {
      comp = this.precomputeWindow(point, W);
      if (W !== 1) {
        if (typeof transform === "function")
          comp = transform(comp);
        pointPrecomputes.set(point, comp);
      }
    }
    return comp;
  }
  cached(point, scalar, transform) {
    const W = getW(point);
    return this.wNAF(W, this.getPrecomputes(W, point, transform), scalar);
  }
  unsafe(point, scalar, transform, prev) {
    const W = getW(point);
    if (W === 1)
      return this._unsafeLadder(point, scalar, prev);
    return this.wNAFUnsafe(W, this.getPrecomputes(W, point, transform), scalar, prev);
  }
  // We calculate precomputes for elliptic curve point multiplication
  // using windowed method. This specifies window size and
  // stores precomputed values. Usually only base point would be precomputed.
  createCache(P, W) {
    validateW(W, this.bits);
    pointWindowSizes.set(P, W);
    pointPrecomputes.delete(P);
  }
  hasCache(elm) {
    return getW(elm) !== 1;
  }
};
function pippenger(c, fieldN, points, scalars) {
  validateMSMPoints(points, c);
  validateMSMScalars(scalars, fieldN);
  const plength = points.length;
  const slength = scalars.length;
  if (plength !== slength)
    throw new Error("arrays of points and scalars must have equal length");
  const zero = c.ZERO;
  const wbits = bitLen(BigInt(plength));
  let windowSize = 1;
  if (wbits > 12)
    windowSize = wbits - 3;
  else if (wbits > 4)
    windowSize = wbits - 2;
  else if (wbits > 0)
    windowSize = 2;
  const MASK = bitMask(windowSize);
  const buckets = new Array(Number(MASK) + 1).fill(zero);
  const lastBits = Math.floor((fieldN.BITS - 1) / windowSize) * windowSize;
  let sum = zero;
  for (let i = lastBits; i >= 0; i -= windowSize) {
    buckets.fill(zero);
    for (let j = 0; j < slength; j++) {
      const scalar = scalars[j];
      const wbits2 = Number(scalar >> BigInt(i) & MASK);
      buckets[wbits2] = buckets[wbits2].add(points[j]);
    }
    let resI = zero;
    for (let j = buckets.length - 1, sumI = zero; j > 0; j--) {
      sumI = sumI.add(buckets[j]);
      resI = resI.add(sumI);
    }
    sum = sum.add(resI);
    if (i !== 0)
      for (let j = 0; j < windowSize; j++)
        sum = sum.double();
  }
  return sum;
}
function createField(order, field, isLE3) {
  if (field) {
    if (field.ORDER !== order)
      throw new Error("Field.ORDER must match order: Fp == p, Fn == n");
    validateField(field);
    return field;
  } else {
    return Field(order, { isLE: isLE3 });
  }
}
function _createCurveFields(type, CURVE, curveOpts = {}, FpFnLE) {
  if (FpFnLE === void 0)
    FpFnLE = type === "edwards";
  if (!CURVE || typeof CURVE !== "object")
    throw new Error(`expected valid ${type} CURVE object`);
  for (const p of ["p", "n", "h"]) {
    const val = CURVE[p];
    if (!(typeof val === "bigint" && val > _0n3))
      throw new Error(`CURVE.${p} must be positive bigint`);
  }
  const Fp2 = createField(CURVE.p, curveOpts.Fp, FpFnLE);
  const Fn2 = createField(CURVE.n, curveOpts.Fn, FpFnLE);
  const _b = type === "weierstrass" ? "b" : "d";
  const params = ["Gx", "Gy", "a", _b];
  for (const p of params) {
    if (!Fp2.isValid(CURVE[p]))
      throw new Error(`CURVE.${p} must be valid field element of CURVE.Fp`);
  }
  CURVE = Object.freeze(Object.assign({}, CURVE));
  return { CURVE, Fp: Fp2, Fn: Fn2 };
}

// node_modules/@noble/curves/esm/abstract/edwards.js
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
var _0n4 = BigInt(0);
var _1n4 = BigInt(1);
var _2n2 = BigInt(2);
var _8n2 = BigInt(8);
function isEdValidXY(Fp2, CURVE, x, y) {
  const x2 = Fp2.sqr(x);
  const y2 = Fp2.sqr(y);
  const left = Fp2.add(Fp2.mul(CURVE.a, x2), y2);
  const right = Fp2.add(Fp2.ONE, Fp2.mul(CURVE.d, Fp2.mul(x2, y2)));
  return Fp2.eql(left, right);
}
function edwards(params, extraOpts = {}) {
  const validated = _createCurveFields("edwards", params, extraOpts, extraOpts.FpFnLE);
  const { Fp: Fp2, Fn: Fn2 } = validated;
  let CURVE = validated.CURVE;
  const { h: cofactor } = CURVE;
  _validateObject(extraOpts, {}, { uvRatio: "function" });
  const MASK = _2n2 << BigInt(Fn2.BYTES * 8) - _1n4;
  const modP = (n) => Fp2.create(n);
  const uvRatio2 = extraOpts.uvRatio || ((u, v) => {
    try {
      return { isValid: true, value: Fp2.sqrt(Fp2.div(u, v)) };
    } catch (e) {
      return { isValid: false, value: _0n4 };
    }
  });
  if (!isEdValidXY(Fp2, CURVE, CURVE.Gx, CURVE.Gy))
    throw new Error("bad curve params: generator point");
  function acoord(title, n, banZero = false) {
    const min = banZero ? _1n4 : _0n4;
    aInRange("coordinate " + title, n, min, MASK);
    return n;
  }
  function aextpoint(other) {
    if (!(other instanceof Point))
      throw new Error("ExtendedPoint expected");
  }
  const toAffineMemo = memoized((p, iz) => {
    const { X, Y, Z } = p;
    const is0 = p.is0();
    if (iz == null)
      iz = is0 ? _8n2 : Fp2.inv(Z);
    const x = modP(X * iz);
    const y = modP(Y * iz);
    const zz = Fp2.mul(Z, iz);
    if (is0)
      return { x: _0n4, y: _1n4 };
    if (zz !== _1n4)
      throw new Error("invZ was invalid");
    return { x, y };
  });
  const assertValidMemo = memoized((p) => {
    const { a, d } = CURVE;
    if (p.is0())
      throw new Error("bad point: ZERO");
    const { X, Y, Z, T } = p;
    const X2 = modP(X * X);
    const Y2 = modP(Y * Y);
    const Z2 = modP(Z * Z);
    const Z4 = modP(Z2 * Z2);
    const aX2 = modP(X2 * a);
    const left = modP(Z2 * modP(aX2 + Y2));
    const right = modP(Z4 + modP(d * modP(X2 * Y2)));
    if (left !== right)
      throw new Error("bad point: equation left != right (1)");
    const XY = modP(X * Y);
    const ZT = modP(Z * T);
    if (XY !== ZT)
      throw new Error("bad point: equation left != right (2)");
    return true;
  });
  class Point {
    constructor(X, Y, Z, T) {
      this.X = acoord("x", X);
      this.Y = acoord("y", Y);
      this.Z = acoord("z", Z, true);
      this.T = acoord("t", T);
      Object.freeze(this);
    }
    static CURVE() {
      return CURVE;
    }
    static fromAffine(p) {
      if (p instanceof Point)
        throw new Error("extended point not allowed");
      const { x, y } = p || {};
      acoord("x", x);
      acoord("y", y);
      return new Point(x, y, _1n4, modP(x * y));
    }
    // Uses algo from RFC8032 5.1.3.
    static fromBytes(bytes, zip215 = false) {
      const len = Fp2.BYTES;
      const { a, d } = CURVE;
      bytes = copyBytes(_abytes2(bytes, len, "point"));
      _abool2(zip215, "zip215");
      const normed = copyBytes(bytes);
      const lastByte = bytes[len - 1];
      normed[len - 1] = lastByte & ~128;
      const y = bytesToNumberLE(normed);
      const max = zip215 ? MASK : Fp2.ORDER;
      aInRange("point.y", y, _0n4, max);
      const y2 = modP(y * y);
      const u = modP(y2 - _1n4);
      const v = modP(d * y2 - a);
      let { isValid, value: x } = uvRatio2(u, v);
      if (!isValid)
        throw new Error("bad point: invalid y coordinate");
      const isXOdd = (x & _1n4) === _1n4;
      const isLastByteOdd = (lastByte & 128) !== 0;
      if (!zip215 && x === _0n4 && isLastByteOdd)
        throw new Error("bad point: x=0 and x_0=1");
      if (isLastByteOdd !== isXOdd)
        x = modP(-x);
      return Point.fromAffine({ x, y });
    }
    static fromHex(bytes, zip215 = false) {
      return Point.fromBytes(ensureBytes("point", bytes), zip215);
    }
    get x() {
      return this.toAffine().x;
    }
    get y() {
      return this.toAffine().y;
    }
    precompute(windowSize = 8, isLazy = true) {
      wnaf.createCache(this, windowSize);
      if (!isLazy)
        this.multiply(_2n2);
      return this;
    }
    // Useful in fromAffine() - not for fromBytes(), which always created valid points.
    assertValidity() {
      assertValidMemo(this);
    }
    // Compare one point to another.
    equals(other) {
      aextpoint(other);
      const { X: X1, Y: Y1, Z: Z1 } = this;
      const { X: X2, Y: Y2, Z: Z2 } = other;
      const X1Z2 = modP(X1 * Z2);
      const X2Z1 = modP(X2 * Z1);
      const Y1Z2 = modP(Y1 * Z2);
      const Y2Z1 = modP(Y2 * Z1);
      return X1Z2 === X2Z1 && Y1Z2 === Y2Z1;
    }
    is0() {
      return this.equals(Point.ZERO);
    }
    negate() {
      return new Point(modP(-this.X), this.Y, this.Z, modP(-this.T));
    }
    // Fast algo for doubling Extended Point.
    // https://hyperelliptic.org/EFD/g1p/auto-twisted-extended.html#doubling-dbl-2008-hwcd
    // Cost: 4M + 4S + 1*a + 6add + 1*2.
    double() {
      const { a } = CURVE;
      const { X: X1, Y: Y1, Z: Z1 } = this;
      const A = modP(X1 * X1);
      const B = modP(Y1 * Y1);
      const C = modP(_2n2 * modP(Z1 * Z1));
      const D = modP(a * A);
      const x1y1 = X1 + Y1;
      const E = modP(modP(x1y1 * x1y1) - A - B);
      const G = D + B;
      const F2 = G - C;
      const H = D - B;
      const X3 = modP(E * F2);
      const Y3 = modP(G * H);
      const T3 = modP(E * H);
      const Z3 = modP(F2 * G);
      return new Point(X3, Y3, Z3, T3);
    }
    // Fast algo for adding 2 Extended Points.
    // https://hyperelliptic.org/EFD/g1p/auto-twisted-extended.html#addition-add-2008-hwcd
    // Cost: 9M + 1*a + 1*d + 7add.
    add(other) {
      aextpoint(other);
      const { a, d } = CURVE;
      const { X: X1, Y: Y1, Z: Z1, T: T1 } = this;
      const { X: X2, Y: Y2, Z: Z2, T: T2 } = other;
      const A = modP(X1 * X2);
      const B = modP(Y1 * Y2);
      const C = modP(T1 * d * T2);
      const D = modP(Z1 * Z2);
      const E = modP((X1 + Y1) * (X2 + Y2) - A - B);
      const F2 = D - C;
      const G = D + C;
      const H = modP(B - a * A);
      const X3 = modP(E * F2);
      const Y3 = modP(G * H);
      const T3 = modP(E * H);
      const Z3 = modP(F2 * G);
      return new Point(X3, Y3, Z3, T3);
    }
    subtract(other) {
      return this.add(other.negate());
    }
    // Constant-time multiplication.
    multiply(scalar) {
      if (!Fn2.isValidNot0(scalar))
        throw new Error("invalid scalar: expected 1 <= sc < curve.n");
      const { p, f } = wnaf.cached(this, scalar, (p2) => normalizeZ(Point, p2));
      return normalizeZ(Point, [p, f])[0];
    }
    // Non-constant-time multiplication. Uses double-and-add algorithm.
    // It's faster, but should only be used when you don't care about
    // an exposed private key e.g. sig verification.
    // Does NOT allow scalars higher than CURVE.n.
    // Accepts optional accumulator to merge with multiply (important for sparse scalars)
    multiplyUnsafe(scalar, acc = Point.ZERO) {
      if (!Fn2.isValid(scalar))
        throw new Error("invalid scalar: expected 0 <= sc < curve.n");
      if (scalar === _0n4)
        return Point.ZERO;
      if (this.is0() || scalar === _1n4)
        return this;
      return wnaf.unsafe(this, scalar, (p) => normalizeZ(Point, p), acc);
    }
    // Checks if point is of small order.
    // If you add something to small order point, you will have "dirty"
    // point with torsion component.
    // Multiplies point by cofactor and checks if the result is 0.
    isSmallOrder() {
      return this.multiplyUnsafe(cofactor).is0();
    }
    // Multiplies point by curve order and checks if the result is 0.
    // Returns `false` is the point is dirty.
    isTorsionFree() {
      return wnaf.unsafe(this, CURVE.n).is0();
    }
    // Converts Extended point to default (x, y) coordinates.
    // Can accept precomputed Z^-1 - for example, from invertBatch.
    toAffine(invertedZ) {
      return toAffineMemo(this, invertedZ);
    }
    clearCofactor() {
      if (cofactor === _1n4)
        return this;
      return this.multiplyUnsafe(cofactor);
    }
    toBytes() {
      const { x, y } = this.toAffine();
      const bytes = Fp2.toBytes(y);
      bytes[bytes.length - 1] |= x & _1n4 ? 128 : 0;
      return bytes;
    }
    toHex() {
      return bytesToHex(this.toBytes());
    }
    toString() {
      return `<Point ${this.is0() ? "ZERO" : this.toHex()}>`;
    }
    // TODO: remove
    get ex() {
      return this.X;
    }
    get ey() {
      return this.Y;
    }
    get ez() {
      return this.Z;
    }
    get et() {
      return this.T;
    }
    static normalizeZ(points) {
      return normalizeZ(Point, points);
    }
    static msm(points, scalars) {
      return pippenger(Point, Fn2, points, scalars);
    }
    _setWindowSize(windowSize) {
      this.precompute(windowSize);
    }
    toRawBytes() {
      return this.toBytes();
    }
  }
  Point.BASE = new Point(CURVE.Gx, CURVE.Gy, _1n4, modP(CURVE.Gx * CURVE.Gy));
  Point.ZERO = new Point(_0n4, _1n4, _1n4, _0n4);
  Point.Fp = Fp2;
  Point.Fn = Fn2;
  const wnaf = new wNAF(Point, Fn2.BITS);
  Point.BASE.precompute(8);
  return Point;
}
var PrimeEdwardsPoint = class {
  constructor(ep) {
    this.ep = ep;
  }
  // Static methods that must be implemented by subclasses
  static fromBytes(_bytes) {
    notImplemented();
  }
  static fromHex(_hex) {
    notImplemented();
  }
  get x() {
    return this.toAffine().x;
  }
  get y() {
    return this.toAffine().y;
  }
  // Common implementations
  clearCofactor() {
    return this;
  }
  assertValidity() {
    this.ep.assertValidity();
  }
  toAffine(invertedZ) {
    return this.ep.toAffine(invertedZ);
  }
  toHex() {
    return bytesToHex(this.toBytes());
  }
  toString() {
    return this.toHex();
  }
  isTorsionFree() {
    return true;
  }
  isSmallOrder() {
    return false;
  }
  add(other) {
    this.assertSame(other);
    return this.init(this.ep.add(other.ep));
  }
  subtract(other) {
    this.assertSame(other);
    return this.init(this.ep.subtract(other.ep));
  }
  multiply(scalar) {
    return this.init(this.ep.multiply(scalar));
  }
  multiplyUnsafe(scalar) {
    return this.init(this.ep.multiplyUnsafe(scalar));
  }
  double() {
    return this.init(this.ep.double());
  }
  negate() {
    return this.init(this.ep.negate());
  }
  precompute(windowSize, isLazy) {
    return this.init(this.ep.precompute(windowSize, isLazy));
  }
  /** @deprecated use `toBytes` */
  toRawBytes() {
    return this.toBytes();
  }
};
function eddsa(Point, cHash, eddsaOpts = {}) {
  if (typeof cHash !== "function")
    throw new Error('"hash" function param is required');
  _validateObject(eddsaOpts, {}, {
    adjustScalarBytes: "function",
    randomBytes: "function",
    domain: "function",
    prehash: "function",
    mapToCurve: "function"
  });
  const { prehash } = eddsaOpts;
  const { BASE, Fp: Fp2, Fn: Fn2 } = Point;
  const randomBytes3 = eddsaOpts.randomBytes || randomBytes;
  const adjustScalarBytes2 = eddsaOpts.adjustScalarBytes || ((bytes) => bytes);
  const domain = eddsaOpts.domain || ((data, ctx, phflag) => {
    _abool2(phflag, "phflag");
    if (ctx.length || phflag)
      throw new Error("Contexts/pre-hash are not supported");
    return data;
  });
  function modN_LE(hash) {
    return Fn2.create(bytesToNumberLE(hash));
  }
  function getPrivateScalar(key) {
    const len = lengths.secretKey;
    key = ensureBytes("private key", key, len);
    const hashed = ensureBytes("hashed private key", cHash(key), 2 * len);
    const head = adjustScalarBytes2(hashed.slice(0, len));
    const prefix = hashed.slice(len, 2 * len);
    const scalar = modN_LE(head);
    return { head, prefix, scalar };
  }
  function getExtendedPublicKey(secretKey) {
    const { head, prefix, scalar } = getPrivateScalar(secretKey);
    const point = BASE.multiply(scalar);
    const pointBytes = point.toBytes();
    return { head, prefix, scalar, point, pointBytes };
  }
  function getPublicKey(secretKey) {
    return getExtendedPublicKey(secretKey).pointBytes;
  }
  function hashDomainToScalar(context = Uint8Array.of(), ...msgs) {
    const msg = concatBytes(...msgs);
    return modN_LE(cHash(domain(msg, ensureBytes("context", context), !!prehash)));
  }
  function sign(msg, secretKey, options = {}) {
    msg = ensureBytes("message", msg);
    if (prehash)
      msg = prehash(msg);
    const { prefix, scalar, pointBytes } = getExtendedPublicKey(secretKey);
    const r = hashDomainToScalar(options.context, prefix, msg);
    const R = BASE.multiply(r).toBytes();
    const k = hashDomainToScalar(options.context, R, pointBytes, msg);
    const s = Fn2.create(r + k * scalar);
    if (!Fn2.isValid(s))
      throw new Error("sign failed: invalid s");
    const rs = concatBytes(R, Fn2.toBytes(s));
    return _abytes2(rs, lengths.signature, "result");
  }
  const verifyOpts = { zip215: true };
  function verify(sig, msg, publicKey, options = verifyOpts) {
    const { context, zip215 } = options;
    const len = lengths.signature;
    sig = ensureBytes("signature", sig, len);
    msg = ensureBytes("message", msg);
    publicKey = ensureBytes("publicKey", publicKey, lengths.publicKey);
    if (zip215 !== void 0)
      _abool2(zip215, "zip215");
    if (prehash)
      msg = prehash(msg);
    const mid = len / 2;
    const r = sig.subarray(0, mid);
    const s = bytesToNumberLE(sig.subarray(mid, len));
    let A, R, SB;
    try {
      A = Point.fromBytes(publicKey, zip215);
      R = Point.fromBytes(r, zip215);
      SB = BASE.multiplyUnsafe(s);
    } catch (error) {
      return false;
    }
    if (!zip215 && A.isSmallOrder())
      return false;
    const k = hashDomainToScalar(context, R.toBytes(), A.toBytes(), msg);
    const RkA = R.add(A.multiplyUnsafe(k));
    return RkA.subtract(SB).clearCofactor().is0();
  }
  const _size = Fp2.BYTES;
  const lengths = {
    secretKey: _size,
    publicKey: _size,
    signature: 2 * _size,
    seed: _size
  };
  function randomSecretKey(seed = randomBytes3(lengths.seed)) {
    return _abytes2(seed, lengths.seed, "seed");
  }
  function keygen(seed) {
    const secretKey = utils.randomSecretKey(seed);
    return { secretKey, publicKey: getPublicKey(secretKey) };
  }
  function isValidSecretKey(key) {
    return isBytes(key) && key.length === Fn2.BYTES;
  }
  function isValidPublicKey(key, zip215) {
    try {
      return !!Point.fromBytes(key, zip215);
    } catch (error) {
      return false;
    }
  }
  const utils = {
    getExtendedPublicKey,
    randomSecretKey,
    isValidSecretKey,
    isValidPublicKey,
    /**
     * Converts ed public key to x public key. Uses formula:
     * - ed25519:
     *   - `(u, v) = ((1+y)/(1-y), sqrt(-486664)*u/x)`
     *   - `(x, y) = (sqrt(-486664)*u/v, (u-1)/(u+1))`
     * - ed448:
     *   - `(u, v) = ((y-1)/(y+1), sqrt(156324)*u/x)`
     *   - `(x, y) = (sqrt(156324)*u/v, (1+u)/(1-u))`
     */
    toMontgomery(publicKey) {
      const { y } = Point.fromBytes(publicKey);
      const size = lengths.publicKey;
      const is25519 = size === 32;
      if (!is25519 && size !== 57)
        throw new Error("only defined for 25519 and 448");
      const u = is25519 ? Fp2.div(_1n4 + y, _1n4 - y) : Fp2.div(y - _1n4, y + _1n4);
      return Fp2.toBytes(u);
    },
    toMontgomerySecret(secretKey) {
      const size = lengths.secretKey;
      _abytes2(secretKey, size);
      const hashed = cHash(secretKey.subarray(0, size));
      return adjustScalarBytes2(hashed).subarray(0, size);
    },
    /** @deprecated */
    randomPrivateKey: randomSecretKey,
    /** @deprecated */
    precompute(windowSize = 8, point = Point.BASE) {
      return point.precompute(windowSize, false);
    }
  };
  return Object.freeze({
    keygen,
    getPublicKey,
    sign,
    verify,
    utils,
    Point,
    lengths
  });
}
function _eddsa_legacy_opts_to_new(c) {
  const CURVE = {
    a: c.a,
    d: c.d,
    p: c.Fp.ORDER,
    n: c.n,
    h: c.h,
    Gx: c.Gx,
    Gy: c.Gy
  };
  const Fp2 = c.Fp;
  const Fn2 = Field(CURVE.n, c.nBitLength, true);
  const curveOpts = { Fp: Fp2, Fn: Fn2, uvRatio: c.uvRatio };
  const eddsaOpts = {
    randomBytes: c.randomBytes,
    adjustScalarBytes: c.adjustScalarBytes,
    domain: c.domain,
    prehash: c.prehash,
    mapToCurve: c.mapToCurve
  };
  return { CURVE, curveOpts, hash: c.hash, eddsaOpts };
}
function _eddsa_new_output_to_legacy(c, eddsa2) {
  const Point = eddsa2.Point;
  const legacy = Object.assign({}, eddsa2, {
    ExtendedPoint: Point,
    CURVE: c,
    nBitLength: Point.Fn.BITS,
    nByteLength: Point.Fn.BYTES
  });
  return legacy;
}
function twistedEdwards(c) {
  const { CURVE, curveOpts, hash, eddsaOpts } = _eddsa_legacy_opts_to_new(c);
  const Point = edwards(CURVE, curveOpts);
  const EDDSA = eddsa(Point, hash, eddsaOpts);
  return _eddsa_new_output_to_legacy(c, EDDSA);
}

// node_modules/@noble/curves/esm/abstract/montgomery.js
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
var _0n5 = BigInt(0);
var _1n5 = BigInt(1);
var _2n3 = BigInt(2);
function validateOpts(curve) {
  _validateObject(curve, {
    adjustScalarBytes: "function",
    powPminus2: "function"
  });
  return Object.freeze({ ...curve });
}
function montgomery(curveDef) {
  const CURVE = validateOpts(curveDef);
  const { P, type, adjustScalarBytes: adjustScalarBytes2, powPminus2, randomBytes: rand } = CURVE;
  const is25519 = type === "x25519";
  if (!is25519 && type !== "x448")
    throw new Error("invalid type");
  const randomBytes_ = rand || randomBytes;
  const montgomeryBits = is25519 ? 255 : 448;
  const fieldLen = is25519 ? 32 : 56;
  const Gu = is25519 ? BigInt(9) : BigInt(5);
  const a24 = is25519 ? BigInt(121665) : BigInt(39081);
  const minScalar = is25519 ? _2n3 ** BigInt(254) : _2n3 ** BigInt(447);
  const maxAdded = is25519 ? BigInt(8) * _2n3 ** BigInt(251) - _1n5 : BigInt(4) * _2n3 ** BigInt(445) - _1n5;
  const maxScalar = minScalar + maxAdded + _1n5;
  const modP = (n) => mod(n, P);
  const GuBytes = encodeU(Gu);
  function encodeU(u) {
    return numberToBytesLE(modP(u), fieldLen);
  }
  function decodeU(u) {
    const _u = ensureBytes("u coordinate", u, fieldLen);
    if (is25519)
      _u[31] &= 127;
    return modP(bytesToNumberLE(_u));
  }
  function decodeScalar(scalar) {
    return bytesToNumberLE(adjustScalarBytes2(ensureBytes("scalar", scalar, fieldLen)));
  }
  function scalarMult(scalar, u) {
    const pu = montgomeryLadder(decodeU(u), decodeScalar(scalar));
    if (pu === _0n5)
      throw new Error("invalid private or public key received");
    return encodeU(pu);
  }
  function scalarMultBase(scalar) {
    return scalarMult(scalar, GuBytes);
  }
  function cswap(swap, x_2, x_3) {
    const dummy = modP(swap * (x_2 - x_3));
    x_2 = modP(x_2 - dummy);
    x_3 = modP(x_3 + dummy);
    return { x_2, x_3 };
  }
  function montgomeryLadder(u, scalar) {
    aInRange("u", u, _0n5, P);
    aInRange("scalar", scalar, minScalar, maxScalar);
    const k = scalar;
    const x_1 = u;
    let x_2 = _1n5;
    let z_2 = _0n5;
    let x_3 = u;
    let z_3 = _1n5;
    let swap = _0n5;
    for (let t = BigInt(montgomeryBits - 1); t >= _0n5; t--) {
      const k_t = k >> t & _1n5;
      swap ^= k_t;
      ({ x_2, x_3 } = cswap(swap, x_2, x_3));
      ({ x_2: z_2, x_3: z_3 } = cswap(swap, z_2, z_3));
      swap = k_t;
      const A = x_2 + z_2;
      const AA = modP(A * A);
      const B = x_2 - z_2;
      const BB = modP(B * B);
      const E = AA - BB;
      const C = x_3 + z_3;
      const D = x_3 - z_3;
      const DA = modP(D * A);
      const CB = modP(C * B);
      const dacb = DA + CB;
      const da_cb = DA - CB;
      x_3 = modP(dacb * dacb);
      z_3 = modP(x_1 * modP(da_cb * da_cb));
      x_2 = modP(AA * BB);
      z_2 = modP(E * (AA + modP(a24 * E)));
    }
    ({ x_2, x_3 } = cswap(swap, x_2, x_3));
    ({ x_2: z_2, x_3: z_3 } = cswap(swap, z_2, z_3));
    const z2 = powPminus2(z_2);
    return modP(x_2 * z2);
  }
  const lengths = {
    secretKey: fieldLen,
    publicKey: fieldLen,
    seed: fieldLen
  };
  const randomSecretKey = (seed = randomBytes_(fieldLen)) => {
    abytes(seed, lengths.seed);
    return seed;
  };
  function keygen(seed) {
    const secretKey = randomSecretKey(seed);
    return { secretKey, publicKey: scalarMultBase(secretKey) };
  }
  const utils = {
    randomSecretKey,
    randomPrivateKey: randomSecretKey
  };
  return {
    keygen,
    getSharedSecret: (secretKey, publicKey) => scalarMult(secretKey, publicKey),
    getPublicKey: (secretKey) => scalarMultBase(secretKey),
    scalarMult,
    scalarMultBase,
    utils,
    GuBytes: GuBytes.slice(),
    lengths
  };
}

// node_modules/@noble/curves/esm/ed25519.js
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
var _0n6 = /* @__PURE__ */ BigInt(0);
var _1n6 = BigInt(1);
var _2n4 = BigInt(2);
var _3n2 = BigInt(3);
var _5n2 = BigInt(5);
var _8n3 = BigInt(8);
var ed25519_CURVE_p = BigInt("0x7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffed");
var ed25519_CURVE = /* @__PURE__ */ (() => ({
  p: ed25519_CURVE_p,
  n: BigInt("0x1000000000000000000000000000000014def9dea2f79cd65812631a5cf5d3ed"),
  h: _8n3,
  a: BigInt("0x7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffec"),
  d: BigInt("0x52036cee2b6ffe738cc740797779e89800700a4d4141d8ab75eb4dca135978a3"),
  Gx: BigInt("0x216936d3cd6e53fec0a4e231fdd6dc5c692cc7609525a7b2c9562d608f25d51a"),
  Gy: BigInt("0x6666666666666666666666666666666666666666666666666666666666666658")
}))();
function ed25519_pow_2_252_3(x) {
  const _10n = BigInt(10), _20n = BigInt(20), _40n = BigInt(40), _80n = BigInt(80);
  const P = ed25519_CURVE_p;
  const x2 = x * x % P;
  const b2 = x2 * x % P;
  const b4 = pow2(b2, _2n4, P) * b2 % P;
  const b5 = pow2(b4, _1n6, P) * x % P;
  const b10 = pow2(b5, _5n2, P) * b5 % P;
  const b20 = pow2(b10, _10n, P) * b10 % P;
  const b40 = pow2(b20, _20n, P) * b20 % P;
  const b80 = pow2(b40, _40n, P) * b40 % P;
  const b160 = pow2(b80, _80n, P) * b80 % P;
  const b240 = pow2(b160, _80n, P) * b80 % P;
  const b250 = pow2(b240, _10n, P) * b10 % P;
  const pow_p_5_8 = pow2(b250, _2n4, P) * x % P;
  return { pow_p_5_8, b2 };
}
function adjustScalarBytes(bytes) {
  bytes[0] &= 248;
  bytes[31] &= 127;
  bytes[31] |= 64;
  return bytes;
}
var ED25519_SQRT_M1 = /* @__PURE__ */ BigInt("19681161376707505956807079304988542015446066515923890162744021073123829784752");
function uvRatio(u, v) {
  const P = ed25519_CURVE_p;
  const v3 = mod(v * v * v, P);
  const v7 = mod(v3 * v3 * v, P);
  const pow = ed25519_pow_2_252_3(u * v7).pow_p_5_8;
  let x = mod(u * v3 * pow, P);
  const vx2 = mod(v * x * x, P);
  const root1 = x;
  const root2 = mod(x * ED25519_SQRT_M1, P);
  const useRoot1 = vx2 === u;
  const useRoot2 = vx2 === mod(-u, P);
  const noRoot = vx2 === mod(-u * ED25519_SQRT_M1, P);
  if (useRoot1)
    x = root1;
  if (useRoot2 || noRoot)
    x = root2;
  if (isNegativeLE(x, P))
    x = mod(-x, P);
  return { isValid: useRoot1 || useRoot2, value: x };
}
var Fp = /* @__PURE__ */ (() => Field(ed25519_CURVE.p, { isLE: true }))();
var Fn = /* @__PURE__ */ (() => Field(ed25519_CURVE.n, { isLE: true }))();
var ed25519Defaults = /* @__PURE__ */ (() => ({
  ...ed25519_CURVE,
  Fp,
  hash: sha512,
  adjustScalarBytes,
  // dom2
  // Ratio of u to v. Allows us to combine inversion and square root. Uses algo from RFC8032 5.1.3.
  // Constant-time, u/√v
  uvRatio
}))();
var ed25519 = /* @__PURE__ */ (() => twistedEdwards(ed25519Defaults))();
var x25519 = /* @__PURE__ */ (() => {
  const P = Fp.ORDER;
  return montgomery({
    P,
    type: "x25519",
    powPminus2: (x) => {
      const { pow_p_5_8, b2 } = ed25519_pow_2_252_3(x);
      return mod(pow2(pow_p_5_8, _3n2, P) * b2, P);
    },
    adjustScalarBytes
  });
})();
var SQRT_M1 = ED25519_SQRT_M1;
var SQRT_AD_MINUS_ONE = /* @__PURE__ */ BigInt("25063068953384623474111414158702152701244531502492656460079210482610430750235");
var INVSQRT_A_MINUS_D = /* @__PURE__ */ BigInt("54469307008909316920995813868745141605393597292927456921205312896311721017578");
var ONE_MINUS_D_SQ = /* @__PURE__ */ BigInt("1159843021668779879193775521855586647937357759715417654439879720876111806838");
var D_MINUS_ONE_SQ = /* @__PURE__ */ BigInt("40440834346308536858101042469323190826248399146238708352240133220865137265952");
var invertSqrt = (number) => uvRatio(_1n6, number);
var MAX_255B = /* @__PURE__ */ BigInt("0x7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
var bytes255ToNumberLE = (bytes) => ed25519.Point.Fp.create(bytesToNumberLE(bytes) & MAX_255B);
function calcElligatorRistrettoMap(r0) {
  const { d } = ed25519_CURVE;
  const P = ed25519_CURVE_p;
  const mod3 = (n) => Fp.create(n);
  const r = mod3(SQRT_M1 * r0 * r0);
  const Ns = mod3((r + _1n6) * ONE_MINUS_D_SQ);
  let c = BigInt(-1);
  const D = mod3((c - d * r) * mod3(r + d));
  let { isValid: Ns_D_is_sq, value: s } = uvRatio(Ns, D);
  let s_ = mod3(s * r0);
  if (!isNegativeLE(s_, P))
    s_ = mod3(-s_);
  if (!Ns_D_is_sq)
    s = s_;
  if (!Ns_D_is_sq)
    c = r;
  const Nt = mod3(c * (r - _1n6) * D_MINUS_ONE_SQ - D);
  const s2 = s * s;
  const W0 = mod3((s + s) * D);
  const W1 = mod3(Nt * SQRT_AD_MINUS_ONE);
  const W2 = mod3(_1n6 - s2);
  const W3 = mod3(_1n6 + s2);
  return new ed25519.Point(mod3(W0 * W3), mod3(W2 * W1), mod3(W1 * W3), mod3(W0 * W2));
}
function ristretto255_map(bytes) {
  abytes(bytes, 64);
  const r1 = bytes255ToNumberLE(bytes.subarray(0, 32));
  const R1 = calcElligatorRistrettoMap(r1);
  const r2 = bytes255ToNumberLE(bytes.subarray(32, 64));
  const R2 = calcElligatorRistrettoMap(r2);
  return new _RistrettoPoint(R1.add(R2));
}
var _RistrettoPoint = class __RistrettoPoint extends PrimeEdwardsPoint {
  constructor(ep) {
    super(ep);
  }
  static fromAffine(ap) {
    return new __RistrettoPoint(ed25519.Point.fromAffine(ap));
  }
  assertSame(other) {
    if (!(other instanceof __RistrettoPoint))
      throw new Error("RistrettoPoint expected");
  }
  init(ep) {
    return new __RistrettoPoint(ep);
  }
  /** @deprecated use `import { ristretto255_hasher } from '@noble/curves/ed25519.js';` */
  static hashToCurve(hex) {
    return ristretto255_map(ensureBytes("ristrettoHash", hex, 64));
  }
  static fromBytes(bytes) {
    abytes(bytes, 32);
    const { a, d } = ed25519_CURVE;
    const P = ed25519_CURVE_p;
    const mod3 = (n) => Fp.create(n);
    const s = bytes255ToNumberLE(bytes);
    if (!equalBytes(Fp.toBytes(s), bytes) || isNegativeLE(s, P))
      throw new Error("invalid ristretto255 encoding 1");
    const s2 = mod3(s * s);
    const u1 = mod3(_1n6 + a * s2);
    const u2 = mod3(_1n6 - a * s2);
    const u1_2 = mod3(u1 * u1);
    const u2_2 = mod3(u2 * u2);
    const v = mod3(a * d * u1_2 - u2_2);
    const { isValid, value: I } = invertSqrt(mod3(v * u2_2));
    const Dx = mod3(I * u2);
    const Dy = mod3(I * Dx * v);
    let x = mod3((s + s) * Dx);
    if (isNegativeLE(x, P))
      x = mod3(-x);
    const y = mod3(u1 * Dy);
    const t = mod3(x * y);
    if (!isValid || isNegativeLE(t, P) || y === _0n6)
      throw new Error("invalid ristretto255 encoding 2");
    return new __RistrettoPoint(new ed25519.Point(x, y, _1n6, t));
  }
  /**
   * Converts ristretto-encoded string to ristretto point.
   * Described in [RFC9496](https://www.rfc-editor.org/rfc/rfc9496#name-decode).
   * @param hex Ristretto-encoded 32 bytes. Not every 32-byte string is valid ristretto encoding
   */
  static fromHex(hex) {
    return __RistrettoPoint.fromBytes(ensureBytes("ristrettoHex", hex, 32));
  }
  static msm(points, scalars) {
    return pippenger(__RistrettoPoint, ed25519.Point.Fn, points, scalars);
  }
  /**
   * Encodes ristretto point to Uint8Array.
   * Described in [RFC9496](https://www.rfc-editor.org/rfc/rfc9496#name-encode).
   */
  toBytes() {
    let { X, Y, Z, T } = this.ep;
    const P = ed25519_CURVE_p;
    const mod3 = (n) => Fp.create(n);
    const u1 = mod3(mod3(Z + Y) * mod3(Z - Y));
    const u2 = mod3(X * Y);
    const u2sq = mod3(u2 * u2);
    const { value: invsqrt } = invertSqrt(mod3(u1 * u2sq));
    const D1 = mod3(invsqrt * u1);
    const D2 = mod3(invsqrt * u2);
    const zInv = mod3(D1 * D2 * T);
    let D;
    if (isNegativeLE(T * zInv, P)) {
      let _x = mod3(Y * SQRT_M1);
      let _y = mod3(X * SQRT_M1);
      X = _x;
      Y = _y;
      D = mod3(D1 * INVSQRT_A_MINUS_D);
    } else {
      D = D2;
    }
    if (isNegativeLE(X * zInv, P))
      Y = mod3(-Y);
    let s = mod3((Z - Y) * D);
    if (isNegativeLE(s, P))
      s = mod3(-s);
    return Fp.toBytes(s);
  }
  /**
   * Compares two Ristretto points.
   * Described in [RFC9496](https://www.rfc-editor.org/rfc/rfc9496#name-equals).
   */
  equals(other) {
    this.assertSame(other);
    const { X: X1, Y: Y1 } = this.ep;
    const { X: X2, Y: Y2 } = other.ep;
    const mod3 = (n) => Fp.create(n);
    const one = mod3(X1 * Y2) === mod3(Y1 * X2);
    const two = mod3(Y1 * Y2) === mod3(X1 * X2);
    return one || two;
  }
  is0() {
    return this.equals(__RistrettoPoint.ZERO);
  }
};
_RistrettoPoint.BASE = /* @__PURE__ */ (() => new _RistrettoPoint(ed25519.Point.BASE))();
_RistrettoPoint.ZERO = /* @__PURE__ */ (() => new _RistrettoPoint(ed25519.Point.ZERO))();
_RistrettoPoint.Fp = /* @__PURE__ */ (() => Fp)();
_RistrettoPoint.Fn = /* @__PURE__ */ (() => Fn)();

// node_modules/@noble/ciphers/esm/utils.js
/*! noble-ciphers - MIT License (c) 2023 Paul Miller (paulmillr.com) */
function isBytes2(a) {
  return a instanceof Uint8Array || ArrayBuffer.isView(a) && a.constructor.name === "Uint8Array";
}
function abool(b) {
  if (typeof b !== "boolean")
    throw new Error(`boolean expected, not ${b}`);
}
function anumber2(n) {
  if (!Number.isSafeInteger(n) || n < 0)
    throw new Error("positive integer expected, got " + n);
}
function abytes2(b, ...lengths) {
  if (!isBytes2(b))
    throw new Error("Uint8Array expected");
  if (lengths.length > 0 && !lengths.includes(b.length))
    throw new Error("Uint8Array expected of length " + lengths + ", got length=" + b.length);
}
function aexists2(instance, checkFinished = true) {
  if (instance.destroyed)
    throw new Error("Hash instance has been destroyed");
  if (checkFinished && instance.finished)
    throw new Error("Hash#digest() has already been called");
}
function aoutput2(out, instance) {
  abytes2(out);
  const min = instance.outputLen;
  if (out.length < min) {
    throw new Error("digestInto() expects output buffer of length at least " + min);
  }
}
function u322(arr) {
  return new Uint32Array(arr.buffer, arr.byteOffset, Math.floor(arr.byteLength / 4));
}
function clean2(...arrays) {
  for (let i = 0; i < arrays.length; i++) {
    arrays[i].fill(0);
  }
}
function createView2(arr) {
  return new DataView(arr.buffer, arr.byteOffset, arr.byteLength);
}
var isLE2 = /* @__PURE__ */ (() => new Uint8Array(new Uint32Array([287454020]).buffer)[0] === 68)();
function utf8ToBytes2(str) {
  if (typeof str !== "string")
    throw new Error("string expected");
  return new Uint8Array(new TextEncoder().encode(str));
}
function toBytes2(data) {
  if (typeof data === "string")
    data = utf8ToBytes2(data);
  else if (isBytes2(data))
    data = copyBytes2(data);
  else
    throw new Error("Uint8Array expected, got " + typeof data);
  return data;
}
function checkOpts(defaults, opts2) {
  if (opts2 == null || typeof opts2 !== "object")
    throw new Error("options must be defined");
  const merged = Object.assign(defaults, opts2);
  return merged;
}
function equalBytes2(a, b) {
  if (a.length !== b.length)
    return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++)
    diff |= a[i] ^ b[i];
  return diff === 0;
}
var wrapCipher = /* @__NO_SIDE_EFFECTS__ */ (params, constructor) => {
  function wrappedCipher(key, ...args) {
    abytes2(key);
    if (!isLE2)
      throw new Error("Non little-endian hardware is not yet supported");
    if (params.nonceLength !== void 0) {
      const nonce = args[0];
      if (!nonce)
        throw new Error("nonce / iv required");
      if (params.varSizeNonce)
        abytes2(nonce);
      else
        abytes2(nonce, params.nonceLength);
    }
    const tagl = params.tagLength;
    if (tagl && args[1] !== void 0) {
      abytes2(args[1]);
    }
    const cipher = constructor(key, ...args);
    const checkOutput = (fnLength, output) => {
      if (output !== void 0) {
        if (fnLength !== 2)
          throw new Error("cipher output not supported");
        abytes2(output);
      }
    };
    let called = false;
    const wrCipher = {
      encrypt(data, output) {
        if (called)
          throw new Error("cannot encrypt() twice with same key + nonce");
        called = true;
        abytes2(data);
        checkOutput(cipher.encrypt.length, output);
        return cipher.encrypt(data, output);
      },
      decrypt(data, output) {
        abytes2(data);
        if (tagl && data.length < tagl)
          throw new Error("invalid ciphertext length: smaller than tagLength=" + tagl);
        checkOutput(cipher.decrypt.length, output);
        return cipher.decrypt(data, output);
      }
    };
    return wrCipher;
  }
  Object.assign(wrappedCipher, params);
  return wrappedCipher;
};
function getOutput(expectedLength, out, onlyAligned = true) {
  if (out === void 0)
    return new Uint8Array(expectedLength);
  if (out.length !== expectedLength)
    throw new Error("invalid output length, expected " + expectedLength + ", got: " + out.length);
  if (onlyAligned && !isAligned32(out))
    throw new Error("invalid output, must be aligned");
  return out;
}
function setBigUint642(view, byteOffset, value, isLE3) {
  if (typeof view.setBigUint64 === "function")
    return view.setBigUint64(byteOffset, value, isLE3);
  const _32n2 = BigInt(32);
  const _u32_max = BigInt(4294967295);
  const wh = Number(value >> _32n2 & _u32_max);
  const wl = Number(value & _u32_max);
  const h = isLE3 ? 4 : 0;
  const l = isLE3 ? 0 : 4;
  view.setUint32(byteOffset + h, wh, isLE3);
  view.setUint32(byteOffset + l, wl, isLE3);
}
function u64Lengths(dataLength, aadLength, isLE3) {
  abool(isLE3);
  const num = new Uint8Array(16);
  const view = createView2(num);
  setBigUint642(view, 0, BigInt(aadLength), isLE3);
  setBigUint642(view, 8, BigInt(dataLength), isLE3);
  return num;
}
function isAligned32(bytes) {
  return bytes.byteOffset % 4 === 0;
}
function copyBytes2(bytes) {
  return Uint8Array.from(bytes);
}

// node_modules/@noble/ciphers/esm/_arx.js
var _utf8ToBytes = (str) => Uint8Array.from(str.split("").map((c) => c.charCodeAt(0)));
var sigma16 = _utf8ToBytes("expand 16-byte k");
var sigma32 = _utf8ToBytes("expand 32-byte k");
var sigma16_32 = u322(sigma16);
var sigma32_32 = u322(sigma32);
function rotl(a, b) {
  return a << b | a >>> 32 - b;
}
function isAligned322(b) {
  return b.byteOffset % 4 === 0;
}
var BLOCK_LEN = 64;
var BLOCK_LEN32 = 16;
var MAX_COUNTER = 2 ** 32 - 1;
var U32_EMPTY = new Uint32Array();
function runCipher(core, sigma, key, nonce, data, output, counter, rounds) {
  const len = data.length;
  const block = new Uint8Array(BLOCK_LEN);
  const b32 = u322(block);
  const isAligned = isAligned322(data) && isAligned322(output);
  const d32 = isAligned ? u322(data) : U32_EMPTY;
  const o32 = isAligned ? u322(output) : U32_EMPTY;
  for (let pos = 0; pos < len; counter++) {
    core(sigma, key, nonce, b32, counter, rounds);
    if (counter >= MAX_COUNTER)
      throw new Error("arx: counter overflow");
    const take = Math.min(BLOCK_LEN, len - pos);
    if (isAligned && take === BLOCK_LEN) {
      const pos32 = pos / 4;
      if (pos % 4 !== 0)
        throw new Error("arx: invalid block position");
      for (let j = 0, posj; j < BLOCK_LEN32; j++) {
        posj = pos32 + j;
        o32[posj] = d32[posj] ^ b32[j];
      }
      pos += BLOCK_LEN;
      continue;
    }
    for (let j = 0, posj; j < take; j++) {
      posj = pos + j;
      output[posj] = data[posj] ^ block[j];
    }
    pos += take;
  }
}
function createCipher(core, opts2) {
  const { allowShortKeys, extendNonceFn, counterLength, counterRight, rounds } = checkOpts({ allowShortKeys: false, counterLength: 8, counterRight: false, rounds: 20 }, opts2);
  if (typeof core !== "function")
    throw new Error("core must be a function");
  anumber2(counterLength);
  anumber2(rounds);
  abool(counterRight);
  abool(allowShortKeys);
  return (key, nonce, data, output, counter = 0) => {
    abytes2(key);
    abytes2(nonce);
    abytes2(data);
    const len = data.length;
    if (output === void 0)
      output = new Uint8Array(len);
    abytes2(output);
    anumber2(counter);
    if (counter < 0 || counter >= MAX_COUNTER)
      throw new Error("arx: counter overflow");
    if (output.length < len)
      throw new Error(`arx: output (${output.length}) is shorter than data (${len})`);
    const toClean = [];
    let l = key.length;
    let k;
    let sigma;
    if (l === 32) {
      toClean.push(k = copyBytes2(key));
      sigma = sigma32_32;
    } else if (l === 16 && allowShortKeys) {
      k = new Uint8Array(32);
      k.set(key);
      k.set(key, 16);
      sigma = sigma16_32;
      toClean.push(k);
    } else {
      throw new Error(`arx: invalid 32-byte key, got length=${l}`);
    }
    if (!isAligned322(nonce))
      toClean.push(nonce = copyBytes2(nonce));
    const k32 = u322(k);
    if (extendNonceFn) {
      if (nonce.length !== 24)
        throw new Error(`arx: extended nonce must be 24 bytes`);
      extendNonceFn(sigma, k32, u322(nonce.subarray(0, 16)), k32);
      nonce = nonce.subarray(16);
    }
    const nonceNcLen = 16 - counterLength;
    if (nonceNcLen !== nonce.length)
      throw new Error(`arx: nonce must be ${nonceNcLen} or 16 bytes`);
    if (nonceNcLen !== 12) {
      const nc = new Uint8Array(12);
      nc.set(nonce, counterRight ? 0 : 12 - nonce.length);
      nonce = nc;
      toClean.push(nonce);
    }
    const n32 = u322(nonce);
    runCipher(core, sigma, k32, n32, data, output, counter, rounds);
    clean2(...toClean);
    return output;
  };
}

// node_modules/@noble/ciphers/esm/_poly1305.js
var u8to16 = (a, i) => a[i++] & 255 | (a[i++] & 255) << 8;
var Poly1305 = class {
  constructor(key) {
    this.blockLen = 16;
    this.outputLen = 16;
    this.buffer = new Uint8Array(16);
    this.r = new Uint16Array(10);
    this.h = new Uint16Array(10);
    this.pad = new Uint16Array(8);
    this.pos = 0;
    this.finished = false;
    key = toBytes2(key);
    abytes2(key, 32);
    const t0 = u8to16(key, 0);
    const t1 = u8to16(key, 2);
    const t2 = u8to16(key, 4);
    const t3 = u8to16(key, 6);
    const t4 = u8to16(key, 8);
    const t5 = u8to16(key, 10);
    const t6 = u8to16(key, 12);
    const t7 = u8to16(key, 14);
    this.r[0] = t0 & 8191;
    this.r[1] = (t0 >>> 13 | t1 << 3) & 8191;
    this.r[2] = (t1 >>> 10 | t2 << 6) & 7939;
    this.r[3] = (t2 >>> 7 | t3 << 9) & 8191;
    this.r[4] = (t3 >>> 4 | t4 << 12) & 255;
    this.r[5] = t4 >>> 1 & 8190;
    this.r[6] = (t4 >>> 14 | t5 << 2) & 8191;
    this.r[7] = (t5 >>> 11 | t6 << 5) & 8065;
    this.r[8] = (t6 >>> 8 | t7 << 8) & 8191;
    this.r[9] = t7 >>> 5 & 127;
    for (let i = 0; i < 8; i++)
      this.pad[i] = u8to16(key, 16 + 2 * i);
  }
  process(data, offset, isLast = false) {
    const hibit = isLast ? 0 : 1 << 11;
    const { h, r } = this;
    const r0 = r[0];
    const r1 = r[1];
    const r2 = r[2];
    const r3 = r[3];
    const r4 = r[4];
    const r5 = r[5];
    const r6 = r[6];
    const r7 = r[7];
    const r8 = r[8];
    const r9 = r[9];
    const t0 = u8to16(data, offset + 0);
    const t1 = u8to16(data, offset + 2);
    const t2 = u8to16(data, offset + 4);
    const t3 = u8to16(data, offset + 6);
    const t4 = u8to16(data, offset + 8);
    const t5 = u8to16(data, offset + 10);
    const t6 = u8to16(data, offset + 12);
    const t7 = u8to16(data, offset + 14);
    let h0 = h[0] + (t0 & 8191);
    let h1 = h[1] + ((t0 >>> 13 | t1 << 3) & 8191);
    let h2 = h[2] + ((t1 >>> 10 | t2 << 6) & 8191);
    let h3 = h[3] + ((t2 >>> 7 | t3 << 9) & 8191);
    let h4 = h[4] + ((t3 >>> 4 | t4 << 12) & 8191);
    let h5 = h[5] + (t4 >>> 1 & 8191);
    let h6 = h[6] + ((t4 >>> 14 | t5 << 2) & 8191);
    let h7 = h[7] + ((t5 >>> 11 | t6 << 5) & 8191);
    let h8 = h[8] + ((t6 >>> 8 | t7 << 8) & 8191);
    let h9 = h[9] + (t7 >>> 5 | hibit);
    let c = 0;
    let d0 = c + h0 * r0 + h1 * (5 * r9) + h2 * (5 * r8) + h3 * (5 * r7) + h4 * (5 * r6);
    c = d0 >>> 13;
    d0 &= 8191;
    d0 += h5 * (5 * r5) + h6 * (5 * r4) + h7 * (5 * r3) + h8 * (5 * r2) + h9 * (5 * r1);
    c += d0 >>> 13;
    d0 &= 8191;
    let d1 = c + h0 * r1 + h1 * r0 + h2 * (5 * r9) + h3 * (5 * r8) + h4 * (5 * r7);
    c = d1 >>> 13;
    d1 &= 8191;
    d1 += h5 * (5 * r6) + h6 * (5 * r5) + h7 * (5 * r4) + h8 * (5 * r3) + h9 * (5 * r2);
    c += d1 >>> 13;
    d1 &= 8191;
    let d2 = c + h0 * r2 + h1 * r1 + h2 * r0 + h3 * (5 * r9) + h4 * (5 * r8);
    c = d2 >>> 13;
    d2 &= 8191;
    d2 += h5 * (5 * r7) + h6 * (5 * r6) + h7 * (5 * r5) + h8 * (5 * r4) + h9 * (5 * r3);
    c += d2 >>> 13;
    d2 &= 8191;
    let d3 = c + h0 * r3 + h1 * r2 + h2 * r1 + h3 * r0 + h4 * (5 * r9);
    c = d3 >>> 13;
    d3 &= 8191;
    d3 += h5 * (5 * r8) + h6 * (5 * r7) + h7 * (5 * r6) + h8 * (5 * r5) + h9 * (5 * r4);
    c += d3 >>> 13;
    d3 &= 8191;
    let d4 = c + h0 * r4 + h1 * r3 + h2 * r2 + h3 * r1 + h4 * r0;
    c = d4 >>> 13;
    d4 &= 8191;
    d4 += h5 * (5 * r9) + h6 * (5 * r8) + h7 * (5 * r7) + h8 * (5 * r6) + h9 * (5 * r5);
    c += d4 >>> 13;
    d4 &= 8191;
    let d5 = c + h0 * r5 + h1 * r4 + h2 * r3 + h3 * r2 + h4 * r1;
    c = d5 >>> 13;
    d5 &= 8191;
    d5 += h5 * r0 + h6 * (5 * r9) + h7 * (5 * r8) + h8 * (5 * r7) + h9 * (5 * r6);
    c += d5 >>> 13;
    d5 &= 8191;
    let d6 = c + h0 * r6 + h1 * r5 + h2 * r4 + h3 * r3 + h4 * r2;
    c = d6 >>> 13;
    d6 &= 8191;
    d6 += h5 * r1 + h6 * r0 + h7 * (5 * r9) + h8 * (5 * r8) + h9 * (5 * r7);
    c += d6 >>> 13;
    d6 &= 8191;
    let d7 = c + h0 * r7 + h1 * r6 + h2 * r5 + h3 * r4 + h4 * r3;
    c = d7 >>> 13;
    d7 &= 8191;
    d7 += h5 * r2 + h6 * r1 + h7 * r0 + h8 * (5 * r9) + h9 * (5 * r8);
    c += d7 >>> 13;
    d7 &= 8191;
    let d8 = c + h0 * r8 + h1 * r7 + h2 * r6 + h3 * r5 + h4 * r4;
    c = d8 >>> 13;
    d8 &= 8191;
    d8 += h5 * r3 + h6 * r2 + h7 * r1 + h8 * r0 + h9 * (5 * r9);
    c += d8 >>> 13;
    d8 &= 8191;
    let d9 = c + h0 * r9 + h1 * r8 + h2 * r7 + h3 * r6 + h4 * r5;
    c = d9 >>> 13;
    d9 &= 8191;
    d9 += h5 * r4 + h6 * r3 + h7 * r2 + h8 * r1 + h9 * r0;
    c += d9 >>> 13;
    d9 &= 8191;
    c = (c << 2) + c | 0;
    c = c + d0 | 0;
    d0 = c & 8191;
    c = c >>> 13;
    d1 += c;
    h[0] = d0;
    h[1] = d1;
    h[2] = d2;
    h[3] = d3;
    h[4] = d4;
    h[5] = d5;
    h[6] = d6;
    h[7] = d7;
    h[8] = d8;
    h[9] = d9;
  }
  finalize() {
    const { h, pad } = this;
    const g = new Uint16Array(10);
    let c = h[1] >>> 13;
    h[1] &= 8191;
    for (let i = 2; i < 10; i++) {
      h[i] += c;
      c = h[i] >>> 13;
      h[i] &= 8191;
    }
    h[0] += c * 5;
    c = h[0] >>> 13;
    h[0] &= 8191;
    h[1] += c;
    c = h[1] >>> 13;
    h[1] &= 8191;
    h[2] += c;
    g[0] = h[0] + 5;
    c = g[0] >>> 13;
    g[0] &= 8191;
    for (let i = 1; i < 10; i++) {
      g[i] = h[i] + c;
      c = g[i] >>> 13;
      g[i] &= 8191;
    }
    g[9] -= 1 << 13;
    let mask = (c ^ 1) - 1;
    for (let i = 0; i < 10; i++)
      g[i] &= mask;
    mask = ~mask;
    for (let i = 0; i < 10; i++)
      h[i] = h[i] & mask | g[i];
    h[0] = (h[0] | h[1] << 13) & 65535;
    h[1] = (h[1] >>> 3 | h[2] << 10) & 65535;
    h[2] = (h[2] >>> 6 | h[3] << 7) & 65535;
    h[3] = (h[3] >>> 9 | h[4] << 4) & 65535;
    h[4] = (h[4] >>> 12 | h[5] << 1 | h[6] << 14) & 65535;
    h[5] = (h[6] >>> 2 | h[7] << 11) & 65535;
    h[6] = (h[7] >>> 5 | h[8] << 8) & 65535;
    h[7] = (h[8] >>> 8 | h[9] << 5) & 65535;
    let f = h[0] + pad[0];
    h[0] = f & 65535;
    for (let i = 1; i < 8; i++) {
      f = (h[i] + pad[i] | 0) + (f >>> 16) | 0;
      h[i] = f & 65535;
    }
    clean2(g);
  }
  update(data) {
    aexists2(this);
    data = toBytes2(data);
    abytes2(data);
    const { buffer, blockLen } = this;
    const len = data.length;
    for (let pos = 0; pos < len; ) {
      const take = Math.min(blockLen - this.pos, len - pos);
      if (take === blockLen) {
        for (; blockLen <= len - pos; pos += blockLen)
          this.process(data, pos);
        continue;
      }
      buffer.set(data.subarray(pos, pos + take), this.pos);
      this.pos += take;
      pos += take;
      if (this.pos === blockLen) {
        this.process(buffer, 0, false);
        this.pos = 0;
      }
    }
    return this;
  }
  destroy() {
    clean2(this.h, this.r, this.buffer, this.pad);
  }
  digestInto(out) {
    aexists2(this);
    aoutput2(out, this);
    this.finished = true;
    const { buffer, h } = this;
    let { pos } = this;
    if (pos) {
      buffer[pos++] = 1;
      for (; pos < 16; pos++)
        buffer[pos] = 0;
      this.process(buffer, 0, true);
    }
    this.finalize();
    let opos = 0;
    for (let i = 0; i < 8; i++) {
      out[opos++] = h[i] >>> 0;
      out[opos++] = h[i] >>> 8;
    }
    return out;
  }
  digest() {
    const { buffer, outputLen } = this;
    this.digestInto(buffer);
    const res = buffer.slice(0, outputLen);
    this.destroy();
    return res;
  }
};
function wrapConstructorWithKey(hashCons) {
  const hashC = (msg, key) => hashCons(key).update(toBytes2(msg)).digest();
  const tmp = hashCons(new Uint8Array(32));
  hashC.outputLen = tmp.outputLen;
  hashC.blockLen = tmp.blockLen;
  hashC.create = (key) => hashCons(key);
  return hashC;
}
var poly1305 = wrapConstructorWithKey((key) => new Poly1305(key));

// node_modules/@noble/ciphers/esm/chacha.js
function chachaCore(s, k, n, out, cnt, rounds = 20) {
  let y00 = s[0], y01 = s[1], y02 = s[2], y03 = s[3], y04 = k[0], y05 = k[1], y06 = k[2], y07 = k[3], y08 = k[4], y09 = k[5], y10 = k[6], y11 = k[7], y12 = cnt, y13 = n[0], y14 = n[1], y15 = n[2];
  let x00 = y00, x01 = y01, x02 = y02, x03 = y03, x04 = y04, x05 = y05, x06 = y06, x07 = y07, x08 = y08, x09 = y09, x10 = y10, x11 = y11, x12 = y12, x13 = y13, x14 = y14, x15 = y15;
  for (let r = 0; r < rounds; r += 2) {
    x00 = x00 + x04 | 0;
    x12 = rotl(x12 ^ x00, 16);
    x08 = x08 + x12 | 0;
    x04 = rotl(x04 ^ x08, 12);
    x00 = x00 + x04 | 0;
    x12 = rotl(x12 ^ x00, 8);
    x08 = x08 + x12 | 0;
    x04 = rotl(x04 ^ x08, 7);
    x01 = x01 + x05 | 0;
    x13 = rotl(x13 ^ x01, 16);
    x09 = x09 + x13 | 0;
    x05 = rotl(x05 ^ x09, 12);
    x01 = x01 + x05 | 0;
    x13 = rotl(x13 ^ x01, 8);
    x09 = x09 + x13 | 0;
    x05 = rotl(x05 ^ x09, 7);
    x02 = x02 + x06 | 0;
    x14 = rotl(x14 ^ x02, 16);
    x10 = x10 + x14 | 0;
    x06 = rotl(x06 ^ x10, 12);
    x02 = x02 + x06 | 0;
    x14 = rotl(x14 ^ x02, 8);
    x10 = x10 + x14 | 0;
    x06 = rotl(x06 ^ x10, 7);
    x03 = x03 + x07 | 0;
    x15 = rotl(x15 ^ x03, 16);
    x11 = x11 + x15 | 0;
    x07 = rotl(x07 ^ x11, 12);
    x03 = x03 + x07 | 0;
    x15 = rotl(x15 ^ x03, 8);
    x11 = x11 + x15 | 0;
    x07 = rotl(x07 ^ x11, 7);
    x00 = x00 + x05 | 0;
    x15 = rotl(x15 ^ x00, 16);
    x10 = x10 + x15 | 0;
    x05 = rotl(x05 ^ x10, 12);
    x00 = x00 + x05 | 0;
    x15 = rotl(x15 ^ x00, 8);
    x10 = x10 + x15 | 0;
    x05 = rotl(x05 ^ x10, 7);
    x01 = x01 + x06 | 0;
    x12 = rotl(x12 ^ x01, 16);
    x11 = x11 + x12 | 0;
    x06 = rotl(x06 ^ x11, 12);
    x01 = x01 + x06 | 0;
    x12 = rotl(x12 ^ x01, 8);
    x11 = x11 + x12 | 0;
    x06 = rotl(x06 ^ x11, 7);
    x02 = x02 + x07 | 0;
    x13 = rotl(x13 ^ x02, 16);
    x08 = x08 + x13 | 0;
    x07 = rotl(x07 ^ x08, 12);
    x02 = x02 + x07 | 0;
    x13 = rotl(x13 ^ x02, 8);
    x08 = x08 + x13 | 0;
    x07 = rotl(x07 ^ x08, 7);
    x03 = x03 + x04 | 0;
    x14 = rotl(x14 ^ x03, 16);
    x09 = x09 + x14 | 0;
    x04 = rotl(x04 ^ x09, 12);
    x03 = x03 + x04 | 0;
    x14 = rotl(x14 ^ x03, 8);
    x09 = x09 + x14 | 0;
    x04 = rotl(x04 ^ x09, 7);
  }
  let oi = 0;
  out[oi++] = y00 + x00 | 0;
  out[oi++] = y01 + x01 | 0;
  out[oi++] = y02 + x02 | 0;
  out[oi++] = y03 + x03 | 0;
  out[oi++] = y04 + x04 | 0;
  out[oi++] = y05 + x05 | 0;
  out[oi++] = y06 + x06 | 0;
  out[oi++] = y07 + x07 | 0;
  out[oi++] = y08 + x08 | 0;
  out[oi++] = y09 + x09 | 0;
  out[oi++] = y10 + x10 | 0;
  out[oi++] = y11 + x11 | 0;
  out[oi++] = y12 + x12 | 0;
  out[oi++] = y13 + x13 | 0;
  out[oi++] = y14 + x14 | 0;
  out[oi++] = y15 + x15 | 0;
}
function hchacha(s, k, i, o32) {
  let x00 = s[0], x01 = s[1], x02 = s[2], x03 = s[3], x04 = k[0], x05 = k[1], x06 = k[2], x07 = k[3], x08 = k[4], x09 = k[5], x10 = k[6], x11 = k[7], x12 = i[0], x13 = i[1], x14 = i[2], x15 = i[3];
  for (let r = 0; r < 20; r += 2) {
    x00 = x00 + x04 | 0;
    x12 = rotl(x12 ^ x00, 16);
    x08 = x08 + x12 | 0;
    x04 = rotl(x04 ^ x08, 12);
    x00 = x00 + x04 | 0;
    x12 = rotl(x12 ^ x00, 8);
    x08 = x08 + x12 | 0;
    x04 = rotl(x04 ^ x08, 7);
    x01 = x01 + x05 | 0;
    x13 = rotl(x13 ^ x01, 16);
    x09 = x09 + x13 | 0;
    x05 = rotl(x05 ^ x09, 12);
    x01 = x01 + x05 | 0;
    x13 = rotl(x13 ^ x01, 8);
    x09 = x09 + x13 | 0;
    x05 = rotl(x05 ^ x09, 7);
    x02 = x02 + x06 | 0;
    x14 = rotl(x14 ^ x02, 16);
    x10 = x10 + x14 | 0;
    x06 = rotl(x06 ^ x10, 12);
    x02 = x02 + x06 | 0;
    x14 = rotl(x14 ^ x02, 8);
    x10 = x10 + x14 | 0;
    x06 = rotl(x06 ^ x10, 7);
    x03 = x03 + x07 | 0;
    x15 = rotl(x15 ^ x03, 16);
    x11 = x11 + x15 | 0;
    x07 = rotl(x07 ^ x11, 12);
    x03 = x03 + x07 | 0;
    x15 = rotl(x15 ^ x03, 8);
    x11 = x11 + x15 | 0;
    x07 = rotl(x07 ^ x11, 7);
    x00 = x00 + x05 | 0;
    x15 = rotl(x15 ^ x00, 16);
    x10 = x10 + x15 | 0;
    x05 = rotl(x05 ^ x10, 12);
    x00 = x00 + x05 | 0;
    x15 = rotl(x15 ^ x00, 8);
    x10 = x10 + x15 | 0;
    x05 = rotl(x05 ^ x10, 7);
    x01 = x01 + x06 | 0;
    x12 = rotl(x12 ^ x01, 16);
    x11 = x11 + x12 | 0;
    x06 = rotl(x06 ^ x11, 12);
    x01 = x01 + x06 | 0;
    x12 = rotl(x12 ^ x01, 8);
    x11 = x11 + x12 | 0;
    x06 = rotl(x06 ^ x11, 7);
    x02 = x02 + x07 | 0;
    x13 = rotl(x13 ^ x02, 16);
    x08 = x08 + x13 | 0;
    x07 = rotl(x07 ^ x08, 12);
    x02 = x02 + x07 | 0;
    x13 = rotl(x13 ^ x02, 8);
    x08 = x08 + x13 | 0;
    x07 = rotl(x07 ^ x08, 7);
    x03 = x03 + x04 | 0;
    x14 = rotl(x14 ^ x03, 16);
    x09 = x09 + x14 | 0;
    x04 = rotl(x04 ^ x09, 12);
    x03 = x03 + x04 | 0;
    x14 = rotl(x14 ^ x03, 8);
    x09 = x09 + x14 | 0;
    x04 = rotl(x04 ^ x09, 7);
  }
  let oi = 0;
  o32[oi++] = x00;
  o32[oi++] = x01;
  o32[oi++] = x02;
  o32[oi++] = x03;
  o32[oi++] = x12;
  o32[oi++] = x13;
  o32[oi++] = x14;
  o32[oi++] = x15;
}
var chacha20 = /* @__PURE__ */ createCipher(chachaCore, {
  counterRight: false,
  counterLength: 4,
  allowShortKeys: false
});
var xchacha20 = /* @__PURE__ */ createCipher(chachaCore, {
  counterRight: false,
  counterLength: 8,
  extendNonceFn: hchacha,
  allowShortKeys: false
});
var ZEROS16 = /* @__PURE__ */ new Uint8Array(16);
var updatePadded = (h, msg) => {
  h.update(msg);
  const left = msg.length % 16;
  if (left)
    h.update(ZEROS16.subarray(left));
};
var ZEROS32 = /* @__PURE__ */ new Uint8Array(32);
function computeTag(fn, key, nonce, data, AAD) {
  const authKey = fn(key, nonce, ZEROS32);
  const h = poly1305.create(authKey);
  if (AAD)
    updatePadded(h, AAD);
  updatePadded(h, data);
  const num = u64Lengths(data.length, AAD ? AAD.length : 0, true);
  h.update(num);
  const res = h.digest();
  clean2(authKey, num);
  return res;
}
var _poly1305_aead = (xorStream) => (key, nonce, AAD) => {
  const tagLength = 16;
  return {
    encrypt(plaintext, output) {
      const plength = plaintext.length;
      output = getOutput(plength + tagLength, output, false);
      output.set(plaintext);
      const oPlain = output.subarray(0, -tagLength);
      xorStream(key, nonce, oPlain, oPlain, 1);
      const tag = computeTag(xorStream, key, nonce, oPlain, AAD);
      output.set(tag, plength);
      clean2(tag);
      return output;
    },
    decrypt(ciphertext, output) {
      output = getOutput(ciphertext.length - tagLength, output, false);
      const data = ciphertext.subarray(0, -tagLength);
      const passedTag = ciphertext.subarray(-tagLength);
      const tag = computeTag(xorStream, key, nonce, data, AAD);
      if (!equalBytes2(passedTag, tag))
        throw new Error("invalid tag");
      output.set(ciphertext.subarray(0, -tagLength));
      xorStream(key, nonce, output, output, 1);
      clean2(tag);
      return output;
    }
  };
};
var chacha20poly1305 = /* @__PURE__ */ wrapCipher({ blockSize: 64, nonceLength: 12, tagLength: 16 }, _poly1305_aead(chacha20));
var xchacha20poly1305 = /* @__PURE__ */ wrapCipher({ blockSize: 64, nonceLength: 24, tagLength: 16 }, _poly1305_aead(xchacha20));

// node_modules/@noble/hashes/esm/hmac.js
var HMAC = class extends Hash {
  constructor(hash, _key) {
    super();
    this.finished = false;
    this.destroyed = false;
    ahash(hash);
    const key = toBytes(_key);
    this.iHash = hash.create();
    if (typeof this.iHash.update !== "function")
      throw new Error("Expected instance of class which extends utils.Hash");
    this.blockLen = this.iHash.blockLen;
    this.outputLen = this.iHash.outputLen;
    const blockLen = this.blockLen;
    const pad = new Uint8Array(blockLen);
    pad.set(key.length > blockLen ? hash.create().update(key).digest() : key);
    for (let i = 0; i < pad.length; i++)
      pad[i] ^= 54;
    this.iHash.update(pad);
    this.oHash = hash.create();
    for (let i = 0; i < pad.length; i++)
      pad[i] ^= 54 ^ 92;
    this.oHash.update(pad);
    clean(pad);
  }
  update(buf) {
    aexists(this);
    this.iHash.update(buf);
    return this;
  }
  digestInto(out) {
    aexists(this);
    abytes(out, this.outputLen);
    this.finished = true;
    this.iHash.digestInto(out);
    this.oHash.update(out);
    this.oHash.digestInto(out);
    this.destroy();
  }
  digest() {
    const out = new Uint8Array(this.oHash.outputLen);
    this.digestInto(out);
    return out;
  }
  _cloneInto(to) {
    to || (to = Object.create(Object.getPrototypeOf(this), {}));
    const { oHash, iHash, finished, destroyed, blockLen, outputLen } = this;
    to = to;
    to.finished = finished;
    to.destroyed = destroyed;
    to.blockLen = blockLen;
    to.outputLen = outputLen;
    to.oHash = oHash._cloneInto(to.oHash);
    to.iHash = iHash._cloneInto(to.iHash);
    return to;
  }
  clone() {
    return this._cloneInto();
  }
  destroy() {
    this.destroyed = true;
    this.oHash.destroy();
    this.iHash.destroy();
  }
};
var hmac = (hash, key, message) => new HMAC(hash, key).update(message).digest();
hmac.create = (hash, key) => new HMAC(hash, key);

// node_modules/@noble/hashes/esm/hkdf.js
function extract(hash, ikm, salt) {
  ahash(hash);
  if (salt === void 0)
    salt = new Uint8Array(hash.outputLen);
  return hmac(hash, toBytes(salt), toBytes(ikm));
}
var HKDF_COUNTER = /* @__PURE__ */ Uint8Array.from([0]);
var EMPTY_BUFFER = /* @__PURE__ */ Uint8Array.of();
function expand(hash, prk, info, length = 32) {
  ahash(hash);
  anumber(length);
  const olen = hash.outputLen;
  if (length > 255 * olen)
    throw new Error("Length should be <= 255*HashLen");
  const blocks = Math.ceil(length / olen);
  if (info === void 0)
    info = EMPTY_BUFFER;
  const okm = new Uint8Array(blocks * olen);
  const HMAC2 = hmac.create(hash, prk);
  const HMACTmp = HMAC2._cloneInto();
  const T = new Uint8Array(HMAC2.outputLen);
  for (let counter = 0; counter < blocks; counter++) {
    HKDF_COUNTER[0] = counter + 1;
    HMACTmp.update(counter === 0 ? EMPTY_BUFFER : T).update(info).update(HKDF_COUNTER).digestInto(T);
    okm.set(T, olen * counter);
    HMAC2._cloneInto(HMACTmp);
  }
  HMAC2.destroy();
  HMACTmp.destroy();
  clean(T, HKDF_COUNTER);
  return okm.slice(0, length);
}
var hkdf = (hash, ikm, salt, info, length) => expand(hash, extract(hash, ikm, salt), info, length);

// node_modules/@noble/hashes/esm/sha3.js
var _0n7 = BigInt(0);
var _1n7 = BigInt(1);
var _2n5 = BigInt(2);
var _7n2 = BigInt(7);
var _256n = BigInt(256);
var _0x71n = BigInt(113);
var SHA3_PI = [];
var SHA3_ROTL = [];
var _SHA3_IOTA = [];
for (let round = 0, R = _1n7, x = 1, y = 0; round < 24; round++) {
  [x, y] = [y, (2 * x + 3 * y) % 5];
  SHA3_PI.push(2 * (5 * y + x));
  SHA3_ROTL.push((round + 1) * (round + 2) / 2 % 64);
  let t = _0n7;
  for (let j = 0; j < 7; j++) {
    R = (R << _1n7 ^ (R >> _7n2) * _0x71n) % _256n;
    if (R & _2n5)
      t ^= _1n7 << (_1n7 << /* @__PURE__ */ BigInt(j)) - _1n7;
  }
  _SHA3_IOTA.push(t);
}
var IOTAS = split(_SHA3_IOTA, true);
var SHA3_IOTA_H = IOTAS[0];
var SHA3_IOTA_L = IOTAS[1];
var rotlH = (h, l, s) => s > 32 ? rotlBH(h, l, s) : rotlSH(h, l, s);
var rotlL = (h, l, s) => s > 32 ? rotlBL(h, l, s) : rotlSL(h, l, s);
function keccakP(s, rounds = 24) {
  const B = new Uint32Array(5 * 2);
  for (let round = 24 - rounds; round < 24; round++) {
    for (let x = 0; x < 10; x++)
      B[x] = s[x] ^ s[x + 10] ^ s[x + 20] ^ s[x + 30] ^ s[x + 40];
    for (let x = 0; x < 10; x += 2) {
      const idx1 = (x + 8) % 10;
      const idx0 = (x + 2) % 10;
      const B0 = B[idx0];
      const B1 = B[idx0 + 1];
      const Th = rotlH(B0, B1, 1) ^ B[idx1];
      const Tl = rotlL(B0, B1, 1) ^ B[idx1 + 1];
      for (let y = 0; y < 50; y += 10) {
        s[x + y] ^= Th;
        s[x + y + 1] ^= Tl;
      }
    }
    let curH = s[2];
    let curL = s[3];
    for (let t = 0; t < 24; t++) {
      const shift = SHA3_ROTL[t];
      const Th = rotlH(curH, curL, shift);
      const Tl = rotlL(curH, curL, shift);
      const PI = SHA3_PI[t];
      curH = s[PI];
      curL = s[PI + 1];
      s[PI] = Th;
      s[PI + 1] = Tl;
    }
    for (let y = 0; y < 50; y += 10) {
      for (let x = 0; x < 10; x++)
        B[x] = s[y + x];
      for (let x = 0; x < 10; x++)
        s[y + x] ^= ~B[(x + 2) % 10] & B[(x + 4) % 10];
    }
    s[0] ^= SHA3_IOTA_H[round];
    s[1] ^= SHA3_IOTA_L[round];
  }
  clean(B);
}
var Keccak = class _Keccak extends Hash {
  // NOTE: we accept arguments in bytes instead of bits here.
  constructor(blockLen, suffix, outputLen, enableXOF = false, rounds = 24) {
    super();
    this.pos = 0;
    this.posOut = 0;
    this.finished = false;
    this.destroyed = false;
    this.enableXOF = false;
    this.blockLen = blockLen;
    this.suffix = suffix;
    this.outputLen = outputLen;
    this.enableXOF = enableXOF;
    this.rounds = rounds;
    anumber(outputLen);
    if (!(0 < blockLen && blockLen < 200))
      throw new Error("only keccak-f1600 function is supported");
    this.state = new Uint8Array(200);
    this.state32 = u32(this.state);
  }
  clone() {
    return this._cloneInto();
  }
  keccak() {
    swap32IfBE(this.state32);
    keccakP(this.state32, this.rounds);
    swap32IfBE(this.state32);
    this.posOut = 0;
    this.pos = 0;
  }
  update(data) {
    aexists(this);
    data = toBytes(data);
    abytes(data);
    const { blockLen, state } = this;
    const len = data.length;
    for (let pos = 0; pos < len; ) {
      const take = Math.min(blockLen - this.pos, len - pos);
      for (let i = 0; i < take; i++)
        state[this.pos++] ^= data[pos++];
      if (this.pos === blockLen)
        this.keccak();
    }
    return this;
  }
  finish() {
    if (this.finished)
      return;
    this.finished = true;
    const { state, suffix, pos, blockLen } = this;
    state[pos] ^= suffix;
    if ((suffix & 128) !== 0 && pos === blockLen - 1)
      this.keccak();
    state[blockLen - 1] ^= 128;
    this.keccak();
  }
  writeInto(out) {
    aexists(this, false);
    abytes(out);
    this.finish();
    const bufferOut = this.state;
    const { blockLen } = this;
    for (let pos = 0, len = out.length; pos < len; ) {
      if (this.posOut >= blockLen)
        this.keccak();
      const take = Math.min(blockLen - this.posOut, len - pos);
      out.set(bufferOut.subarray(this.posOut, this.posOut + take), pos);
      this.posOut += take;
      pos += take;
    }
    return out;
  }
  xofInto(out) {
    if (!this.enableXOF)
      throw new Error("XOF is not possible for this instance");
    return this.writeInto(out);
  }
  xof(bytes) {
    anumber(bytes);
    return this.xofInto(new Uint8Array(bytes));
  }
  digestInto(out) {
    aoutput(out, this);
    if (this.finished)
      throw new Error("digest() was already called");
    this.writeInto(out);
    this.destroy();
    return out;
  }
  digest() {
    return this.digestInto(new Uint8Array(this.outputLen));
  }
  destroy() {
    this.destroyed = true;
    clean(this.state);
  }
  _cloneInto(to) {
    const { blockLen, suffix, outputLen, rounds, enableXOF } = this;
    to || (to = new _Keccak(blockLen, suffix, outputLen, enableXOF, rounds));
    to.state32.set(this.state32);
    to.pos = this.pos;
    to.posOut = this.posOut;
    to.finished = this.finished;
    to.rounds = rounds;
    to.suffix = suffix;
    to.outputLen = outputLen;
    to.enableXOF = enableXOF;
    to.destroyed = this.destroyed;
    return to;
  }
};
var gen = (suffix, blockLen, outputLen) => createHasher(() => new Keccak(blockLen, suffix, outputLen));
var sha3_224 = /* @__PURE__ */ (() => gen(6, 144, 224 / 8))();
var sha3_256 = /* @__PURE__ */ (() => gen(6, 136, 256 / 8))();
var sha3_384 = /* @__PURE__ */ (() => gen(6, 104, 384 / 8))();
var sha3_512 = /* @__PURE__ */ (() => gen(6, 72, 512 / 8))();
var genShake = (suffix, blockLen, outputLen) => createXOFer((opts2 = {}) => new Keccak(blockLen, suffix, opts2.dkLen === void 0 ? outputLen : opts2.dkLen, true));
var shake128 = /* @__PURE__ */ (() => genShake(31, 168, 128 / 8))();
var shake256 = /* @__PURE__ */ (() => genShake(31, 136, 256 / 8))();

// node_modules/@noble/post-quantum/esm/utils.js
/*! noble-post-quantum - MIT License (c) 2024 Paul Miller (paulmillr.com) */
var ensureBytes2 = abytes;
var randomBytes2 = randomBytes;
function equalBytes3(a, b) {
  if (a.length !== b.length)
    return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++)
    diff |= a[i] ^ b[i];
  return diff === 0;
}
function splitCoder(...lengths) {
  const getLength = (c) => typeof c === "number" ? c : c.bytesLen;
  const bytesLen = lengths.reduce((sum, a) => sum + getLength(a), 0);
  return {
    bytesLen,
    encode: (bufs) => {
      const res = new Uint8Array(bytesLen);
      for (let i = 0, pos = 0; i < lengths.length; i++) {
        const c = lengths[i];
        const l = getLength(c);
        const b = typeof c === "number" ? bufs[i] : c.encode(bufs[i]);
        ensureBytes2(b, l);
        res.set(b, pos);
        if (typeof c !== "number")
          b.fill(0);
        pos += l;
      }
      return res;
    },
    decode: (buf) => {
      ensureBytes2(buf, bytesLen);
      const res = [];
      for (const c of lengths) {
        const l = getLength(c);
        const b = buf.subarray(0, l);
        res.push(typeof c === "number" ? b : c.decode(b));
        buf = buf.subarray(l);
      }
      return res;
    }
  };
}
function vecCoder(c, vecLen) {
  const bytesLen = vecLen * c.bytesLen;
  return {
    bytesLen,
    encode: (u) => {
      if (u.length !== vecLen)
        throw new Error(`vecCoder.encode: wrong length=${u.length}. Expected: ${vecLen}`);
      const res = new Uint8Array(bytesLen);
      for (let i = 0, pos = 0; i < u.length; i++) {
        const b = c.encode(u[i]);
        res.set(b, pos);
        b.fill(0);
        pos += b.length;
      }
      return res;
    },
    decode: (a) => {
      ensureBytes2(a, bytesLen);
      const r = [];
      for (let i = 0; i < a.length; i += c.bytesLen)
        r.push(c.decode(a.subarray(i, i + c.bytesLen)));
      return r;
    }
  };
}
function cleanBytes(...list) {
  for (const t of list) {
    if (Array.isArray(t))
      for (const b of t)
        b.fill(0);
    else
      t.fill(0);
  }
}
function getMask(bits) {
  return (1 << bits) - 1;
}
var EMPTY = new Uint8Array(0);
var HASHES = {
  "SHA2-256": { oid: hexToBytes("0609608648016503040201"), hash: sha256 },
  "SHA2-384": { oid: hexToBytes("0609608648016503040202"), hash: sha384 },
  "SHA2-512": { oid: hexToBytes("0609608648016503040203"), hash: sha512 },
  "SHA2-224": { oid: hexToBytes("0609608648016503040204"), hash: sha224 },
  "SHA2-512/224": { oid: hexToBytes("0609608648016503040205"), hash: sha512_224 },
  "SHA2-512/256": { oid: hexToBytes("0609608648016503040206"), hash: sha512_256 },
  "SHA3-224": { oid: hexToBytes("0609608648016503040207"), hash: sha3_224 },
  "SHA3-256": { oid: hexToBytes("0609608648016503040208"), hash: sha3_256 },
  "SHA3-384": { oid: hexToBytes("0609608648016503040209"), hash: sha3_384 },
  "SHA3-512": { oid: hexToBytes("060960864801650304020A"), hash: sha3_512 },
  "SHAKE-128": {
    oid: hexToBytes("060960864801650304020B"),
    hash: (msg) => shake128(msg, { dkLen: 32 })
  },
  "SHAKE-256": {
    oid: hexToBytes("060960864801650304020C"),
    hash: (msg) => shake256(msg, { dkLen: 64 })
  }
};

// node_modules/@noble/post-quantum/esm/_crystals.js
/*! noble-post-quantum - MIT License (c) 2024 Paul Miller (paulmillr.com) */
function bitReversal(n, bits = 8) {
  const padded = n.toString(2).padStart(8, "0");
  const sliced = padded.slice(-bits).padStart(7, "0");
  const revrsd = sliced.split("").reverse().join("");
  return Number.parseInt(revrsd, 2);
}
var genCrystals = (opts2) => {
  const { newPoly, N: N2, Q: Q2, F: F2, ROOT_OF_UNITY: ROOT_OF_UNITY2, brvBits, isKyber } = opts2;
  const mod3 = (a, modulo = Q2) => {
    const result = a % modulo | 0;
    return (result >= 0 ? result | 0 : modulo + result | 0) | 0;
  };
  const smod = (a, modulo = Q2) => {
    const r = mod3(a, modulo) | 0;
    return (r > modulo >> 1 ? r - modulo | 0 : r) | 0;
  };
  function getZettas() {
    const out = newPoly(N2);
    for (let i = 0; i < N2; i++) {
      const b = bitReversal(i, brvBits);
      const p = BigInt(ROOT_OF_UNITY2) ** BigInt(b) % BigInt(Q2);
      out[i] = Number(p) | 0;
    }
    return out;
  }
  const nttZetas2 = getZettas();
  const LEN1 = isKyber ? 128 : N2;
  const LEN2 = isKyber ? 1 : 0;
  const NTT2 = {
    encode: (r) => {
      for (let k = 1, len = 128; len > LEN2; len >>= 1) {
        for (let start = 0; start < N2; start += 2 * len) {
          const zeta = nttZetas2[k++];
          for (let j = start; j < start + len; j++) {
            const t = mod3(zeta * r[j + len]);
            r[j + len] = mod3(r[j] - t) | 0;
            r[j] = mod3(r[j] + t) | 0;
          }
        }
      }
      return r;
    },
    decode: (r) => {
      for (let k = LEN1 - 1, len = 1 + LEN2; len < LEN1 + LEN2; len <<= 1) {
        for (let start = 0; start < N2; start += 2 * len) {
          const zeta = nttZetas2[k--];
          for (let j = start; j < start + len; j++) {
            const t = r[j];
            r[j] = mod3(t + r[j + len]);
            r[j + len] = mod3(zeta * (r[j + len] - t));
          }
        }
      }
      for (let i = 0; i < r.length; i++)
        r[i] = mod3(F2 * r[i]);
      return r;
    }
  };
  const bitsCoder2 = (d, c) => {
    const mask = getMask(d);
    const bytesLen = d * (N2 / 8);
    return {
      bytesLen,
      encode: (poly) => {
        const r = new Uint8Array(bytesLen);
        for (let i = 0, buf = 0, bufLen = 0, pos = 0; i < poly.length; i++) {
          buf |= (c.encode(poly[i]) & mask) << bufLen;
          bufLen += d;
          for (; bufLen >= 8; bufLen -= 8, buf >>= 8)
            r[pos++] = buf & getMask(bufLen);
        }
        return r;
      },
      decode: (bytes) => {
        const r = newPoly(N2);
        for (let i = 0, buf = 0, bufLen = 0, pos = 0; i < bytes.length; i++) {
          buf |= bytes[i] << bufLen;
          bufLen += 8;
          for (; bufLen >= d; bufLen -= d, buf >>= d)
            r[pos++] = c.decode(buf & mask);
        }
        return r;
      }
    };
  };
  return { mod: mod3, smod, nttZetas: nttZetas2, NTT: NTT2, bitsCoder: bitsCoder2 };
};
var createXofShake = (shake) => (seed, blockLen) => {
  if (!blockLen)
    blockLen = shake.blockLen;
  const _seed = new Uint8Array(seed.length + 2);
  _seed.set(seed);
  const seedLen = seed.length;
  const buf = new Uint8Array(blockLen);
  let h = shake.create({});
  let calls = 0;
  let xofs = 0;
  return {
    stats: () => ({ calls, xofs }),
    get: (x, y) => {
      _seed[seedLen + 0] = x;
      _seed[seedLen + 1] = y;
      h.destroy();
      h = shake.create({}).update(_seed);
      calls++;
      return () => {
        xofs++;
        return h.xofInto(buf);
      };
    },
    clean: () => {
      h.destroy();
      buf.fill(0);
      _seed.fill(0);
    }
  };
};
var XOF128 = /* @__PURE__ */ createXofShake(shake128);

// node_modules/@noble/post-quantum/esm/ml-kem.js
/*! noble-post-quantum - MIT License (c) 2024 Paul Miller (paulmillr.com) */
var N = 256;
var Q = 3329;
var F = 3303;
var ROOT_OF_UNITY = 17;
var { mod: mod2, nttZetas, NTT, bitsCoder } = genCrystals({
  N,
  Q,
  F,
  ROOT_OF_UNITY,
  newPoly: (n) => new Uint16Array(n),
  brvBits: 7,
  isKyber: true
});
var PARAMS = {
  512: { N, Q, K: 2, ETA1: 3, ETA2: 2, du: 10, dv: 4, RBGstrength: 128 },
  768: { N, Q, K: 3, ETA1: 2, ETA2: 2, du: 10, dv: 4, RBGstrength: 192 },
  1024: { N, Q, K: 4, ETA1: 2, ETA2: 2, du: 11, dv: 5, RBGstrength: 256 }
};
var compress = (d) => {
  if (d >= 12)
    return { encode: (i) => i, decode: (i) => i };
  const a = 2 ** (d - 1);
  return {
    // const compress = (i: number) => round((2 ** d / Q) * i) % 2 ** d;
    encode: (i) => ((i << d) + Q / 2) / Q,
    // const decompress = (i: number) => round((Q / 2 ** d) * i);
    decode: (i) => i * Q + a >>> d
  };
};
var polyCoder = (d) => bitsCoder(d, compress(d));
function polyAdd(a, b) {
  for (let i = 0; i < N; i++)
    a[i] = mod2(a[i] + b[i]);
}
function polySub(a, b) {
  for (let i = 0; i < N; i++)
    a[i] = mod2(a[i] - b[i]);
}
function BaseCaseMultiply(a0, a1, b0, b1, zeta) {
  const c0 = mod2(a1 * b1 * zeta + a0 * b0);
  const c1 = mod2(a0 * b1 + a1 * b0);
  return { c0, c1 };
}
function MultiplyNTTs(f, g) {
  for (let i = 0; i < N / 2; i++) {
    let z = nttZetas[64 + (i >> 1)];
    if (i & 1)
      z = -z;
    const { c0, c1 } = BaseCaseMultiply(f[2 * i + 0], f[2 * i + 1], g[2 * i + 0], g[2 * i + 1], z);
    f[2 * i + 0] = c0;
    f[2 * i + 1] = c1;
  }
  return f;
}
function SampleNTT(xof) {
  const r = new Uint16Array(N);
  for (let j = 0; j < N; ) {
    const b = xof();
    if (b.length % 3)
      throw new Error("SampleNTT: unaligned block");
    for (let i = 0; j < N && i + 3 <= b.length; i += 3) {
      const d1 = (b[i + 0] >> 0 | b[i + 1] << 8) & 4095;
      const d2 = (b[i + 1] >> 4 | b[i + 2] << 4) & 4095;
      if (d1 < Q)
        r[j++] = d1;
      if (j < N && d2 < Q)
        r[j++] = d2;
    }
  }
  return r;
}
function sampleCBD(PRF, seed, nonce, eta) {
  const buf = PRF(eta * N / 4, seed, nonce);
  const r = new Uint16Array(N);
  const b32 = u32(buf);
  let len = 0;
  for (let i = 0, p = 0, bb = 0, t0 = 0; i < b32.length; i++) {
    let b = b32[i];
    for (let j = 0; j < 32; j++) {
      bb += b & 1;
      b >>= 1;
      len += 1;
      if (len === eta) {
        t0 = bb;
        bb = 0;
      } else if (len === 2 * eta) {
        r[p++] = mod2(t0 - bb);
        bb = 0;
        len = 0;
      }
    }
  }
  if (len)
    throw new Error(`sampleCBD: leftover bits: ${len}`);
  return r;
}
var genKPKE = (opts2) => {
  const { K, PRF, XOF, HASH512, ETA1, ETA2, du, dv } = opts2;
  const poly1 = polyCoder(1);
  const polyV = polyCoder(dv);
  const polyU = polyCoder(du);
  const publicCoder = splitCoder(vecCoder(polyCoder(12), K), 32);
  const secretCoder = vecCoder(polyCoder(12), K);
  const cipherCoder = splitCoder(vecCoder(polyU, K), polyV);
  const seedCoder = splitCoder(32, 32);
  return {
    secretCoder,
    secretKeyLen: secretCoder.bytesLen,
    publicKeyLen: publicCoder.bytesLen,
    cipherTextLen: cipherCoder.bytesLen,
    keygen: (seed) => {
      ensureBytes2(seed, 32);
      const seedDst = new Uint8Array(33);
      seedDst.set(seed);
      seedDst[32] = K;
      const seedHash = HASH512(seedDst);
      const [rho, sigma] = seedCoder.decode(seedHash);
      const sHat = [];
      const tHat = [];
      for (let i = 0; i < K; i++)
        sHat.push(NTT.encode(sampleCBD(PRF, sigma, i, ETA1)));
      const x = XOF(rho);
      for (let i = 0; i < K; i++) {
        const e = NTT.encode(sampleCBD(PRF, sigma, K + i, ETA1));
        for (let j = 0; j < K; j++) {
          const aji = SampleNTT(x.get(j, i));
          polyAdd(e, MultiplyNTTs(aji, sHat[j]));
        }
        tHat.push(e);
      }
      x.clean();
      const res = {
        publicKey: publicCoder.encode([tHat, rho]),
        secretKey: secretCoder.encode(sHat)
      };
      cleanBytes(rho, sigma, sHat, tHat, seedDst, seedHash);
      return res;
    },
    encrypt: (publicKey, msg, seed) => {
      const [tHat, rho] = publicCoder.decode(publicKey);
      const rHat = [];
      for (let i = 0; i < K; i++)
        rHat.push(NTT.encode(sampleCBD(PRF, seed, i, ETA1)));
      const x = XOF(rho);
      const tmp2 = new Uint16Array(N);
      const u = [];
      for (let i = 0; i < K; i++) {
        const e1 = sampleCBD(PRF, seed, K + i, ETA2);
        const tmp = new Uint16Array(N);
        for (let j = 0; j < K; j++) {
          const aij = SampleNTT(x.get(i, j));
          polyAdd(tmp, MultiplyNTTs(aij, rHat[j]));
        }
        polyAdd(e1, NTT.decode(tmp));
        u.push(e1);
        polyAdd(tmp2, MultiplyNTTs(tHat[i], rHat[i]));
        tmp.fill(0);
      }
      x.clean();
      const e2 = sampleCBD(PRF, seed, 2 * K, ETA2);
      polyAdd(e2, NTT.decode(tmp2));
      const v = poly1.decode(msg);
      polyAdd(v, e2);
      cleanBytes(tHat, rHat, tmp2, e2);
      return cipherCoder.encode([u, v]);
    },
    decrypt: (cipherText, privateKey) => {
      const [u, v] = cipherCoder.decode(cipherText);
      const sk = secretCoder.decode(privateKey);
      const tmp = new Uint16Array(N);
      for (let i = 0; i < K; i++)
        polyAdd(tmp, MultiplyNTTs(sk[i], NTT.encode(u[i])));
      polySub(v, NTT.decode(tmp));
      cleanBytes(tmp, sk, u);
      return poly1.encode(v);
    }
  };
};
function createKyber(opts2) {
  const KPKE = genKPKE(opts2);
  const { HASH256, HASH512, KDF } = opts2;
  const { secretCoder: KPKESecretCoder, cipherTextLen } = KPKE;
  const publicKeyLen = KPKE.publicKeyLen;
  const secretCoder = splitCoder(KPKE.secretKeyLen, KPKE.publicKeyLen, 32, 32);
  const secretKeyLen = secretCoder.bytesLen;
  const msgLen = 32;
  return {
    publicKeyLen,
    msgLen,
    keygen: (seed = randomBytes2(64)) => {
      ensureBytes2(seed, 64);
      const { publicKey, secretKey: sk } = KPKE.keygen(seed.subarray(0, 32));
      const publicKeyHash = HASH256(publicKey);
      const secretKey = secretCoder.encode([sk, publicKey, publicKeyHash, seed.subarray(32)]);
      cleanBytes(sk, publicKeyHash);
      return { publicKey, secretKey };
    },
    encapsulate: (publicKey, msg = randomBytes2(32)) => {
      ensureBytes2(publicKey, publicKeyLen);
      ensureBytes2(msg, msgLen);
      const eke = publicKey.subarray(0, 384 * opts2.K);
      const ek = KPKESecretCoder.encode(KPKESecretCoder.decode(eke.slice()));
      if (!equalBytes3(ek, eke)) {
        cleanBytes(ek);
        throw new Error("ML-KEM.encapsulate: wrong publicKey modulus");
      }
      cleanBytes(ek);
      const kr = HASH512.create().update(msg).update(HASH256(publicKey)).digest();
      const cipherText = KPKE.encrypt(publicKey, msg, kr.subarray(32, 64));
      kr.subarray(32).fill(0);
      return { cipherText, sharedSecret: kr.subarray(0, 32) };
    },
    decapsulate: (cipherText, secretKey) => {
      ensureBytes2(secretKey, secretKeyLen);
      ensureBytes2(cipherText, cipherTextLen);
      const [sk, publicKey, publicKeyHash, z] = secretCoder.decode(secretKey);
      const msg = KPKE.decrypt(cipherText, sk);
      const kr = HASH512.create().update(msg).update(publicKeyHash).digest();
      const Khat = kr.subarray(0, 32);
      const cipherText2 = KPKE.encrypt(publicKey, msg, kr.subarray(32, 64));
      const isValid = equalBytes3(cipherText, cipherText2);
      const Kbar = KDF.create({ dkLen: 32 }).update(z).update(cipherText).digest();
      cleanBytes(msg, cipherText2, !isValid ? Khat : Kbar);
      return isValid ? Khat : Kbar;
    }
  };
}
function shakePRF(dkLen, key, nonce) {
  return shake256.create({ dkLen }).update(key).update(new Uint8Array([nonce])).digest();
}
var opts = {
  HASH256: sha3_256,
  HASH512: sha3_512,
  KDF: shake256,
  XOF: XOF128,
  PRF: shakePRF
};
var ml_kem512 = /* @__PURE__ */ createKyber({
  ...opts,
  ...PARAMS[512]
});
var ml_kem768 = /* @__PURE__ */ createKyber({
  ...opts,
  ...PARAMS[768]
});
var ml_kem1024 = /* @__PURE__ */ createKyber({
  ...opts,
  ...PARAMS[1024]
});

// cripto.js
var te = new TextEncoder();
var td = new TextDecoder();
var MAX_SKIP = 1e3;
var MAX_CHIAVI_SALTATE = 2e3;
var MASSIMO_TESTO = 4e3;
var BLOCCHI = [256, 512, 1024, 2048, 4096, 8192];
function casuali(n) {
  const b = new Uint8Array(n);
  globalThis.crypto.getRandomValues(b);
  return b;
}
function concat(...parti) {
  let tot = 0;
  for (const p of parti) tot += p.length;
  const r = new Uint8Array(tot);
  let o = 0;
  for (const p of parti) {
    r.set(p, o);
    o += p.length;
  }
  return r;
}
function inHex(b) {
  return Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
}
function daHex(s) {
  if (typeof s !== "string" || s.length % 2 || /[^0-9a-f]/i.test(s)) throw new Error("esadecimale non valido");
  const r = new Uint8Array(s.length / 2);
  for (let i = 0; i < r.length; i++) r[i] = parseInt(s.substr(i * 2, 2), 16);
  return r;
}
function inB64(b) {
  let s = "";
  for (let i = 0; i < b.length; i += 32768) s += String.fromCharCode.apply(null, b.subarray(i, i + 32768));
  return btoa(s);
}
function daB64(s) {
  const bin = atob(s);
  const r = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) r[i] = bin.charCodeAt(i);
  return r;
}
function uguali(a, b) {
  if (a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a[i] ^ b[i];
  return d === 0;
}
function u323(n) {
  const b = new Uint8Array(4);
  new DataView(b.buffer).setUint32(0, n >>> 0, true);
  return b;
}
function leggiU32(b, o) {
  return new DataView(b.buffer, b.byteOffset + o, 4).getUint32(0, true);
}
function kdf(salt, ikm, info, len) {
  return hkdf(sha256, ikm, salt, typeof info === "string" ? te.encode(info) : info, len);
}
var ZERI32 = new Uint8Array(32);
function dh(priv, pub) {
  const r = x25519.getSharedSecret(priv, pub);
  let tutto0 = 0;
  for (const x of r) tutto0 |= x;
  if (tutto0 === 0) throw new Error("chiave pubblica non valida");
  return r;
}
function nuovaCoppia() {
  const priv = x25519.utils.randomPrivateKey();
  return { priv, pub: x25519.getPublicKey(priv) };
}
function aeadCifra(chiave, nonce, ad, testo) {
  return chacha20poly1305(chiave, nonce, ad).encrypt(testo);
}
function aeadDecifra(chiave, nonce, ad, cifrato) {
  return chacha20poly1305(chiave, nonce, ad).decrypt(cifrato);
}
function creaIdentita() {
  return nuovaCoppia();
}
var ALFABETO = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
function inBase32(b) {
  let bits = 0, val = 0, out = "";
  for (const x of b) {
    val = val << 8 | x;
    bits += 8;
    while (bits >= 5) {
      out += ALFABETO[val >>> bits - 5 & 31];
      bits -= 5;
    }
  }
  if (bits > 0) out += ALFABETO[val << 5 - bits & 31];
  return out;
}
function daBase32(s) {
  let bits = 0, val = 0;
  const out = [];
  for (const c of s) {
    const i = ALFABETO.indexOf(c);
    if (i < 0) throw new Error("carattere non valido nel codice");
    val = val << 5 | i;
    bits += 5;
    if (bits >= 8) {
      out.push(val >>> bits - 8 & 255);
      bits -= 8;
    }
  }
  return Uint8Array.from(out);
}
function creaInvito(identita) {
  const mb = casuali(16), rd = casuali(32), tk = casuali(32);
  const corpo = concat(identita.pub, mb, tk);
  const controllo = sha256(concat(te.encode("WGC1"), corpo)).slice(0, 4);
  const testo = inBase32(concat(corpo, controllo));
  const gruppi = testo.match(/.{1,6}/g).join("-");
  return { codice: "WGC1-" + gruppi, mb: inHex(mb), rd: inHex(rd), tk: inHex(tk) };
}
function leggiCodice(testo) {
  let s = String(testo).toUpperCase().replace(/[\s-]/g, "");
  if (!s.startsWith("WGC1")) throw new Error("Il codice non è valido: deve cominciare con WGC1.");
  s = s.slice(4);
  let b;
  try {
    b = daBase32(s);
  } catch (e) {
    throw new Error("Il codice contiene caratteri non validi.");
  }
  if (b.length < 84) throw new Error("Il codice è incompleto.");
  b = b.slice(0, 84);
  const corpo = b.slice(0, 80);
  const controllo = sha256(concat(te.encode("WGC1"), corpo)).slice(0, 4);
  if (!uguali(controllo, b.slice(80, 84))) throw new Error("Il codice non è corretto: controlla di averlo scritto giusto.");
  return { ikPub: corpo.slice(0, 32), mb: inHex(corpo.slice(32, 48)), tk: inHex(corpo.slice(48, 80)) };
}
function numeroDiSicurezza(ikA, ikB) {
  const [a, b] = inHex(ikA) < inHex(ikB) ? [ikA, ikB] : [ikB, ikA];
  const d = kdf(ZERI32, concat(a, b), "WGChat-sicurezza", 48);
  let cifre = "";
  for (let i = 0; i < 12; i++) {
    const n = (d[i * 4] << 24 | d[i * 4 + 1] << 16 | d[i * 4 + 2] << 8 | d[i * 4 + 3]) >>> 0;
    cifre += String(n % 1e5).padStart(5, "0");
  }
  return cifre.match(/.{5}/g).join(" ");
}
var Simm = class _Simm {
  constructor() {
    const nome = te.encode("WGChat/1 Noise_IK_25519+MLKEM768_ChaChaPoly_SHA256");
    this.ck = sha256(nome);
    this.h = sha256(concat(this.ck, te.encode("WGChat/1")));
    this.k = null;
    this.n = 0;
  }
  static daJson(o) {
    const s = new _Simm();
    s.ck = daHex(o.ck);
    s.h = daHex(o.h);
    s.k = o.k ? daHex(o.k) : null;
    s.n = o.n;
    return s;
  }
  inJson() {
    return { ck: inHex(this.ck), h: inHex(this.h), k: this.k ? inHex(this.k) : null, n: this.n };
  }
  mixHash(d) {
    this.h = sha256(concat(this.h, d));
  }
  mixKey(dhOut) {
    const o = kdf(this.ck, dhOut, "mk", 64);
    this.ck = o.slice(0, 32);
    this.k = o.slice(32);
    this.n = 0;
  }
  nonce() {
    const n = new Uint8Array(12);
    new DataView(n.buffer).setUint32(4, this.n, true);
    return n;
  }
  cifra(testo) {
    if (!this.k) throw new Error("nessuna chiave");
    const c = aeadCifra(this.k, this.nonce(), this.h, testo);
    this.n++;
    this.mixHash(c);
    return c;
  }
  decifra(c) {
    if (!this.k) throw new Error("nessuna chiave");
    const t = aeadDecifra(this.k, this.nonce(), this.h, c);
    this.n++;
    this.mixHash(c);
    return t;
  }
};
var T_STRETTA1 = 1;
var T_STRETTA2 = 2;
var T_MSG = 3;
var T_MSG_INIZIALE = 4;
function jsonBytes(o) {
  return te.encode(JSON.stringify(o));
}
function bytesJson(b) {
  return JSON.parse(td.decode(b));
}
function iniziaStretta(identitaI, codiceR, dati) {
  const s = new Simm();
  s.mixHash(codiceR.ikPub);
  const e = nuovaCoppia();
  s.mixHash(e.pub);
  s.mixKey(dh(e.priv, codiceR.ikPub));
  const c1 = s.cifra(identitaI.pub);
  s.mixKey(dh(identitaI.priv, codiceR.ikPub));
  const c2 = s.cifra(imbottisci(jsonBytes(dati)));
  const msg1 = concat(Uint8Array.of(T_STRETTA1), e.pub, c1, c2);
  return { msg1, stato: { simm: s.inJson(), ePriv: inHex(e.priv), ePub: inHex(e.pub), ikR: inHex(codiceR.ikPub) } };
}
function riceviStretta1(identitaR, msg1) {
  if (msg1[0] !== T_STRETTA1 || msg1.length < 1 + 32 + 48 + 16) throw new Error("richiesta non valida");
  const s = new Simm();
  s.mixHash(identitaR.pub);
  const eI = msg1.slice(1, 33);
  s.mixHash(eI);
  s.mixKey(dh(identitaR.priv, eI));
  const ikI = s.decifra(msg1.slice(33, 81));
  s.mixKey(dh(identitaR.priv, ikI));
  const dati = bytesJson(spoglia(s.decifra(msg1.slice(81))));
  return { ikI, dati, stato: { simm: s.inJson(), eIPub: inHex(eI), ikI: inHex(ikI) } };
}
function rispondiStretta(statoR, dati) {
  const s = Simm.daJson(statoR.simm);
  const eI = daHex(statoR.eIPub), ikI = daHex(statoR.ikI);
  const e = nuovaCoppia();
  const kem = ml_kem768.keygen();
  s.mixHash(e.pub);
  s.mixKey(dh(e.priv, eI));
  s.mixKey(dh(e.priv, ikI));
  const c3 = s.cifra(imbottisci(jsonBytes({ ...dati, ek: inB64(kem.publicKey) })));
  const msg2 = concat(Uint8Array.of(T_STRETTA2), e.pub, c3);
  return { msg2, stato: { simm: s.inJson(), ePriv: inHex(e.priv), ePub: inHex(e.pub), kemSk: inHex(kem.secretKey), ikI: statoR.ikI } };
}
function finisciStretta(identitaI, statoI, msg2) {
  if (msg2[0] !== T_STRETTA2 || msg2.length < 1 + 32 + 16) throw new Error("risposta non valida");
  const s = Simm.daJson(statoI.simm);
  const eR = msg2.slice(1, 33);
  s.mixHash(eR);
  s.mixKey(dh(daHex(statoI.ePriv), eR));
  s.mixKey(dh(identitaI.priv, eR));
  const dati = bytesJson(spoglia(s.decifra(msg2.slice(33))));
  const ek = daB64(dati.ek);
  if (ek.length !== 1184) throw new Error("chiave non valida");
  delete dati.ek;
  const enc = ml_kem768.encapsulate(ek);
  const sk = kdf(s.ck, enc.sharedSecret, concat(te.encode("WGChat-sk"), s.h), 32);
  const sess = iniziaRatchetAlice(sk, s.h, eR);
  sess.prefisso = inHex(enc.cipherText);
  sess.ikRemota = statoI.ikR;
  return { sessione: sess, dati };
}
function accettaPrimoMessaggio(statoR, blob) {
  if (blob[0] !== T_MSG_INIZIALE || blob.length < 1 + 1088 + 68 + 16) throw new Error("messaggio non valido");
  const s = Simm.daJson(statoR.simm);
  const ss = ml_kem768.decapsulate(blob.slice(1, 1089), daHex(statoR.kemSk));
  const sk = kdf(s.ck, ss, concat(te.encode("WGChat-sk"), s.h), 32);
  const sess = iniziaRatchetBob(sk, s.h, { priv: daHex(statoR.ePriv), pub: daHex(statoR.ePub) });
  sess.ikRemota = statoR.ikI;
  const testo = decifra(sess, blob);
  return { sessione: sess, testo };
}
function kdfRk(rk, dhOut) {
  const o = kdf(rk, dhOut, "WGChat-rk", 96);
  return [o.slice(0, 32), o.slice(32, 64), o.slice(64, 96)];
}
function kdfCk(ck) {
  return [hmac(sha256, ck, Uint8Array.of(2)), hmac(sha256, ck, Uint8Array.of(1))];
}
function hCifra(hk, intestazione) {
  const nonce = casuali(12);
  return concat(nonce, aeadCifra(hk, nonce, new Uint8Array(0), intestazione));
}
function hDecifra(hk, eh) {
  if (!hk) return null;
  try {
    return aeadDecifra(hk, eh.slice(0, 12), new Uint8Array(0), eh.slice(12));
  } catch (e) {
    return null;
  }
}
function chiaveMessaggio(mk) {
  const o = kdf(ZERI32, mk, "WGChat-mk", 44);
  return [o.slice(0, 32), o.slice(32, 44)];
}
var LUNG_EH = 12 + 40 + 16;
function iniziaRatchetAlice(sk, ad, dhrPub) {
  const hka = kdf(ZERI32, sk, "WGChat-hka", 32), nhkb = kdf(ZERI32, sk, "WGChat-nhkb", 32);
  const dhs = nuovaCoppia();
  const [rk, cks, nhks] = kdfRk(sk, dh(dhs.priv, dhrPub));
  return {
    ad: inHex(ad),
    dhsPriv: inHex(dhs.priv),
    dhsPub: inHex(dhs.pub),
    dhr: inHex(dhrPub),
    rk: inHex(rk),
    cks: inHex(cks),
    ckr: null,
    ns: 0,
    nr: 0,
    pn: 0,
    hks: inHex(hka),
    hkr: null,
    nhks: inHex(nhks),
    nhkr: inHex(nhkb),
    saltate: {},
    prefisso: null,
    ikRemota: null
  };
}
function iniziaRatchetBob(sk, ad, coppia) {
  const hka = kdf(ZERI32, sk, "WGChat-hka", 32), nhkb = kdf(ZERI32, sk, "WGChat-nhkb", 32);
  return {
    ad: inHex(ad),
    dhsPriv: inHex(coppia.priv),
    dhsPub: inHex(coppia.pub),
    dhr: null,
    rk: inHex(sk),
    cks: null,
    ckr: null,
    ns: 0,
    nr: 0,
    pn: 0,
    hks: null,
    hkr: null,
    nhks: inHex(nhkb),
    nhkr: inHex(hka),
    saltate: {},
    prefisso: null,
    ikRemota: null
  };
}
function imbottisci(testo) {
  const tot = 2 + testo.length;
  const dim = BLOCCHI.find((b) => b >= tot);
  if (!dim) throw new Error("Il messaggio è troppo lungo.");
  const r = new Uint8Array(dim);
  r[0] = testo.length >> 8;
  r[1] = testo.length & 255;
  r.set(testo, 2);
  return r;
}
function spoglia(b) {
  const n = b[0] << 8 | b[1];
  if (2 + n > b.length) throw new Error("riempimento non valido");
  return b.slice(2, 2 + n);
}
function cifra(sess, testoBytes) {
  const ad = daHex(sess.ad);
  const [ck2, mk] = kdfCk(daHex(sess.cks));
  sess.cks = inHex(ck2);
  const intestazione = concat(daHex(sess.dhsPub), u323(sess.pn), u323(sess.ns));
  const eh = hCifra(daHex(sess.hks), intestazione);
  sess.ns += 1;
  const [k, nonce] = chiaveMessaggio(mk);
  const corpo = aeadCifra(k, nonce, concat(ad, eh), imbottisci(testoBytes));
  if (sess.prefisso) return concat(Uint8Array.of(T_MSG_INIZIALE), daHex(sess.prefisso), eh, corpo);
  return concat(Uint8Array.of(T_MSG), eh, corpo);
}
function copia(o) {
  return JSON.parse(JSON.stringify(o));
}
function decifra(sess, blob) {
  if (blob.length < 1 + LUNG_EH + 16) throw new Error("messaggio troppo corto");
  let inizio = 1;
  if (blob[0] === T_MSG_INIZIALE) inizio = 1 + 1088;
  else if (blob[0] !== T_MSG) throw new Error("tipo di messaggio sconosciuto");
  const eh = blob.slice(inizio, inizio + LUNG_EH);
  const corpo = blob.slice(inizio + LUNG_EH);
  const st = copia(sess);
  const testo = decifraHE(st, eh, corpo);
  st.prefisso = null;
  Object.assign(sess, st);
  return testo;
}
function decifraHE(st, eh, corpo) {
  const ad = daHex(st.ad);
  for (const chiave of Object.keys(st.saltate)) {
    const [hkHex, nStr] = chiave.split(":");
    const intest = hDecifra(daHex(hkHex), eh);
    if (intest && leggiU32(intest, 36) === Number(nStr)) {
      const mk2 = daHex(st.saltate[chiave]);
      const [k2, nonce2] = chiaveMessaggio(mk2);
      const t = spoglia(aeadDecifra(k2, nonce2, concat(ad, eh), corpo));
      delete st.saltate[chiave];
      return t;
    }
  }
  let intestazione = hDecifra(st.hkr ? daHex(st.hkr) : null, eh), nuovoDh = false;
  if (!intestazione) {
    intestazione = hDecifra(st.nhkr ? daHex(st.nhkr) : null, eh);
    nuovoDh = true;
  }
  if (!intestazione) throw new Error("messaggio non decifrabile");
  const dhPub = intestazione.slice(0, 32), pn = leggiU32(intestazione, 32), n = leggiU32(intestazione, 36);
  if (nuovoDh) {
    salta(st, pn);
    st.pn = st.ns;
    st.ns = 0;
    st.nr = 0;
    st.hks = st.nhks;
    st.hkr = st.nhkr;
    st.dhr = inHex(dhPub);
    let o = kdfRk(daHex(st.rk), dh(daHex(st.dhsPriv), dhPub));
    st.rk = inHex(o[0]);
    st.ckr = inHex(o[1]);
    st.nhkr = inHex(o[2]);
    const nuova = nuovaCoppia();
    st.dhsPriv = inHex(nuova.priv);
    st.dhsPub = inHex(nuova.pub);
    o = kdfRk(daHex(st.rk), dh(nuova.priv, dhPub));
    st.rk = inHex(o[0]);
    st.cks = inHex(o[1]);
    st.nhks = inHex(o[2]);
  }
  salta(st, n);
  const [ck2, mk] = kdfCk(daHex(st.ckr));
  st.ckr = inHex(ck2);
  st.nr += 1;
  const [k, nonce] = chiaveMessaggio(mk);
  return spoglia(aeadDecifra(k, nonce, concat(ad, eh), corpo));
}
function salta(st, fino) {
  if (st.nr + MAX_SKIP < fino) throw new Error("troppi messaggi saltati");
  if (!st.ckr) return;
  while (st.nr < fino) {
    const [ck2, mk] = kdfCk(daHex(st.ckr));
    st.ckr = inHex(ck2);
    st.saltate[st.hkr + ":" + st.nr] = inHex(mk);
    st.nr += 1;
  }
  const chiavi = Object.keys(st.saltate);
  if (chiavi.length > MAX_CHIAVI_SALTATE) for (const c of chiavi.slice(0, chiavi.length - MAX_CHIAVI_SALTATE)) delete st.saltate[c];
}
function testoAByte(oggetto) {
  return jsonBytes(oggetto);
}
function byteATesto(b) {
  return bytesJson(b);
}
function shaHex(b) {
  return inHex(sha256(b));
}
function tipoBlocco(blob) {
  switch (blob[0]) {
    case T_STRETTA1:
      return "stretta1";
    case T_STRETTA2:
      return "stretta2";
    case T_MSG:
    case T_MSG_INIZIALE:
      return "messaggio";
    default:
      return "sconosciuto";
  }
}

// motore.js
var GIORNI = 864e5;
var SCADENZA_CODA = 14 * GIORNI;
var MAX_CONTATTI = 200;
var MAX_VISTI = 300;
var RIPROVA_OK = 0;
function pulisciNome(n) {
  return String(n ?? "").replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 30);
}
function esadecimale(s, n) {
  return typeof s === "string" && s.length === n && /^[0-9a-f]+$/i.test(s);
}
function nuovoId() {
  return inHex(casuali(8));
}
function statoVuoto(nome) {
  const id = creaIdentita();
  return { v: 1, ident: { priv: inHex(id.priv), pub: inHex(id.pub) }, nome: pulisciNome(nome) || "Io", inviti: [], contatti: [], coda: [], bloccati: [] };
}
var MotoreChat = class {
  /**
   * @param {object} o  stato: lo stato salvato (o null per cominciare); trasporto: { invia(mb, tk, b64) -> 'ok'|'no', ascolta(cassette, ack, attesa) -> [{id, mb, b}] };
   *                    salvaStato(stato): salva lo stato; salvaMessaggi(cid, elenco): salva i messaggi di una conversazione; messaggi: { cid: [...] }; ora: () => ms
   */
  constructor(o) {
    this.s = o.stato || statoVuoto(o.nome);
    if (!Array.isArray(this.s.bloccati)) this.s.bloccati = [];
    if (o.nome && o.stato && this.s.nome !== pulisciNome(o.nome)) this.s.nome = pulisciNome(o.nome);
    this.messaggi = o.messaggi || {};
    this.trasporto = o.trasporto;
    this._salvaStato = o.salvaStato || (async () => {
    });
    this._salvaMessaggi = o.salvaMessaggi || (async () => {
    });
    this.ora = o.ora || (() => Date.now());
    this.ascoltatori = {};
    this.ackPendenti = [];
    this.attivo = false;
    this.collegato = false;
  }
  // ---------------------------------------------------------------- eventi
  su(tipo, f) {
    var _a;
    ((_a = this.ascoltatori)[tipo] || (_a[tipo] = [])).push(f);
  }
  emetti(tipo, dati) {
    for (const f of this.ascoltatori[tipo] || []) {
      try {
        f(dati);
      } catch (e) {
      }
    }
  }
  get identita() {
    return { priv: daHex(this.s.ident.priv), pub: daHex(this.s.ident.pub) };
  }
  contatto(cid) {
    return this.s.contatti.find((k) => k.id === cid);
  }
  async salva() {
    await this._salvaStato(this.s);
  }
  // ---------------------------------------------------------------- inviti e contatti
  async cambiaNome(nome) {
    this.s.nome = pulisciNome(nome) || this.s.nome;
    await this.salva();
  }
  async nuovoInvito() {
    const inv = creaInvito(this.identita);
    inv.creato = this.ora();
    this.s.inviti.push(inv);
    if (this.s.inviti.length > 5) this.s.inviti.shift();
    await this.salva();
    return inv;
  }
  async eliminaInvito(mb) {
    this.s.inviti = this.s.inviti.filter((i) => i.mb !== mb);
    await this.salva();
  }
  /** Aggiunge un contatto con il suo codice: parte la richiesta, che sarà consegnata appena l'altro è collegato. */
  async aggiungiContatto(codiceTesto, utente = "") {
    if (this.s.contatti.length >= MAX_CONTATTI) throw new Error("Hai già troppi contatti.");
    const codice = leggiCodice(codiceTesto);
    if (uguali(codice.ikPub, this.identita.pub)) throw new Error("Questo è il tuo codice: devi usare quello di un'altra persona.");
    const ikHex = inHex(codice.ikPub);
    if (this.s.bloccati.some((b) => b.ik === ikHex)) throw new Error("Hai bloccato questa persona: prima sbloccala.");
    const esistente = this.s.contatti.find((k2) => k2.ikRemota === ikHex && k2.stato !== "rifiutato");
    if (esistente) throw new Error("Hai già questo contatto: " + this.nomeVisibile(esistente) + ".");
    const mia = { mb: inHex(casuali(16)), rd: inHex(casuali(32)), tk: inHex(casuali(32)) };
    const r = iniziaStretta(this.identita, codice, { n: this.s.nome, mb: mia.mb, tk: mia.tk });
    const k = {
      id: nuovoId(),
      nome: pulisciNome(utente) || "Nuovo contatto",
      utente: pulisciNome(utente),
      alias: "",
      stato: "invitato",
      ikRemota: ikHex,
      mia,
      verso: { mb: codice.mb, tk: codice.tk },
      stretta: r.stato,
      rstato: null,
      sessione: null,
      verificato: false,
      visti: {},
      nonLetti: 0,
      creato: this.ora()
    };
    this.s.contatti.push(k);
    this.accodaBlocco(k, "stretta", r.msg1, null);
    await this.salva();
    this.emetti("contatto", k);
    return k;
  }
  async accetta(cid) {
    const k = this.contatto(cid);
    if (!k || k.stato !== "richiesta") throw new Error("Nessuna richiesta da accettare.");
    k.mia = { mb: inHex(casuali(16)), rd: inHex(casuali(32)), tk: inHex(casuali(32)) };
    const r = rispondiStretta(k.rstato, { n: this.s.nome, mb: k.mia.mb, tk: k.mia.tk });
    k.rstato = r.stato;
    k.stato = "accettato";
    this.accodaBlocco(k, "stretta2", r.msg2, null);
    await this.salva();
    this.emetti("contatto", k);
  }
  async rifiuta(cid) {
    await this.elimina(cid);
  }
  /**
   * Blocca una persona: non riceve più niente da lei, le sue richieste vengono ignorate e non le arriva alcun segnale (i suoi messaggi
   * restano "in attesa", come se non fossi collegato). Restituisce il nome utente, se si conosce, per bloccarlo anche nei giochi.
   */
  async blocca(cid) {
    const k = this.contatto(cid);
    if (!k) return "";
    if (!this.s.bloccati.some((b) => b.ik === k.ikRemota)) this.s.bloccati.push({ ik: k.ikRemota, nome: this.nomeVisibile(k), utente: k.utente || "" });
    k.bloccato = true;
    this.s.coda = this.s.coda.filter((q) => q.cid !== cid);
    await this.salva();
    this.emetti("contatto", k);
    return k.utente || "";
  }
  /** Sblocca per chiave d'identità (resta valido anche se il contatto era stato eliminato). */
  async sblocca(ik) {
    const b = this.s.bloccati.find((x) => x.ik === ik);
    this.s.bloccati = this.s.bloccati.filter((x) => x.ik !== ik);
    const k = this.s.contatti.find((x) => x.ikRemota === ik);
    if (k) k.bloccato = false;
    await this.salva();
    this.emetti("contatto", k || null);
    return b ? b.utente || "" : "";
  }
  elencoBloccati() {
    return this.s.bloccati;
  }
  async elimina(cid) {
    this.s.contatti = this.s.contatti.filter((k) => k.id !== cid);
    this.s.coda = this.s.coda.filter((q) => q.cid !== cid);
    delete this.messaggi[cid];
    await this.salva();
    await this._salvaMessaggi(cid, null);
    this.emetti("contatto", null);
  }
  async segnaVerificato(cid, valore = true) {
    const k = this.contatto(cid);
    if (k) {
      k.verificato = valore;
      await this.salva();
      this.emetti("contatto", k);
    }
  }
  async rinomina(cid, alias) {
    const k = this.contatto(cid);
    if (k) {
      k.alias = pulisciNome(alias);
      await this.salva();
      this.emetti("contatto", k);
    }
  }
  nomeVisibile(k) {
    return k.alias || k.nome;
  }
  numeroDiSicurezza(cid) {
    const k = this.contatto(cid);
    return k ? numeroDiSicurezza(this.identita.pub, daHex(k.ikRemota)) : "";
  }
  elencoMessaggi(cid) {
    return this.messaggi[cid] || [];
  }
  async segnaLetti(cid) {
    const k = this.contatto(cid);
    if (k && k.nonLetti) {
      k.nonLetti = 0;
      await this.salva();
      this.emetti("contatto", k);
    }
  }
  // ---------------------------------------------------------------- invio
  accodaBlocco(k, tipo, blob, mid) {
    this.s.coda.push({ cid: k.id, tipo, mid, b64: inB64(blob), verso: { ...k.verso }, tentativi: 0, prossimo: 0, creato: this.ora() });
  }
  accodaMessaggio(k, oggetto, tipo) {
    const blob = cifra(k.sessione, testoAByte(oggetto));
    this.accodaBlocco(k, tipo, blob, oggetto.id || null);
  }
  /** Scrive un messaggio: si cifra subito e resta in coda finché l'altro non lo riceve. */
  async scrivi(cid, testo) {
    var _a;
    const k = this.contatto(cid);
    if (k && k.bloccato) throw new Error("Hai bloccato questa persona: sbloccala per scriverle.");
    if (!k || !k.sessione || !k.sessione.cks) throw new Error("Questa conversazione non è ancora pronta: aspetta che l'altro si colleghi.");
    testo = String(testo).replace(/\r\n/g, "\n").trim();
    if (!testo) throw new Error("Scrivi qualcosa.");
    if (testo.length > MASSIMO_TESTO) throw new Error("Il messaggio è troppo lungo: al massimo " + MASSIMO_TESTO + " caratteri.");
    const id = nuovoId();
    this.accodaMessaggio(k, { t: "m", id, x: testo }, "msg");
    const m = { id, dir: "out", ts: this.ora(), x: testo, st: "attesa" };
    ((_a = this.messaggi)[cid] || (_a[cid] = [])).push(m);
    await this.salva();
    await this._salvaMessaggi(cid, this.messaggi[cid]);
    this.emetti("messaggio", { cid, m });
    this.sollecita();
    return m;
  }
  /** Prova a consegnare quello che è in coda. Restituisce quanti blocchi sono stati accettati dal relè. */
  async inviaCoda() {
    if (this._invio) return 0;
    this._invio = true;
    try {
      return await this._inviaCoda();
    } finally {
      this._invio = false;
    }
  }
  /** Fa partire subito la consegna della coda (per esempio dopo aver scritto), senza aspettare il prossimo giro. */
  sollecita() {
    this.inviaCoda().catch(() => {
    });
  }
  async _inviaCoda() {
    const adesso = this.ora();
    let accettati = 0, cambiato = false;
    for (const q of [...this.s.coda]) {
      const k = this.contatto(q.cid);
      if (!k || k.bloccato) {
        this.s.coda = this.s.coda.filter((x) => x !== q);
        cambiato = true;
        continue;
      }
      if (adesso - q.creato > SCADENZA_CODA) {
        this.s.coda = this.s.coda.filter((x) => x !== q);
        const m = (this.messaggi[q.cid] || []).find((x) => x.id === q.mid);
        if (m && m.st !== "consegnato") {
          m.st = "scaduto";
          await this._salvaMessaggi(q.cid, this.messaggi[q.cid]);
          this.emetti("messaggio", { cid: q.cid, m });
        }
        cambiato = true;
        continue;
      }
      if (q.prossimo > adesso) continue;
      let r;
      try {
        r = await this.trasporto.invia(q.verso.mb, q.verso.tk, q.b64);
      } catch (e) {
        r = "errore";
      }
      if (r === "ok") {
        accettati++;
        if (q.tipo === "ack") {
          this.s.coda = this.s.coda.filter((x) => x !== q);
        } else {
          q.prossimo = adesso + RIPROVA_OK;
          q.tentativi = 0;
          const m = (this.messaggi[q.cid] || []).find((x) => x.id === q.mid);
          if (m && m.st === "attesa") {
            m.st = "inviato";
            await this._salvaMessaggi(q.cid, this.messaggi[q.cid]);
            this.emetti("messaggio", { cid: q.cid, m });
          }
        }
      } else {
        q.tentativi++;
        q.prossimo = adesso;
      }
      cambiato = true;
    }
    if (cambiato) await this.salva();
    return accettati;
  }
  // ---------------------------------------------------------------- ricezione
  cassette() {
    const r = [];
    for (const i of this.s.inviti) r.push({ mb: i.mb, rd: i.rd, th: shaHex(daHex(i.tk)) });
    for (const k of this.s.contatti) if (k.mia && k.stato !== "richiesta" && !k.bloccato) r.push({ mb: k.mia.mb, rd: k.mia.rd, th: shaHex(daHex(k.mia.tk)) });
    return r;
  }
  /** Una richiesta all'ascolto: chiede al relè se c'è qualcosa e lo elabora. Restituisce quanti blocchi sono arrivati. */
  async ascolta(attesa) {
    const cassette = this.cassette();
    if (!cassette.length) return 0;
    const ack = this.ackPendenti;
    this.ackPendenti = [];
    let blocchi;
    try {
      blocchi = await this.trasporto.ascolta(cassette, ack, attesa);
      this.collegato = true;
    } catch (e) {
      this.ackPendenti = ack.concat(this.ackPendenti);
      if (this.collegato) {
        this.collegato = false;
        this.emetti("collegamento", false);
      }
      throw e;
    }
    for (const b of blocchi) {
      try {
        await this.gestisciBlocco(b);
      } catch (e) {
      }
      this.ackPendenti.push(b.id);
    }
    if (blocchi.length) await this.salva();
    return blocchi.length;
  }
  async gestisciBlocco(b) {
    let blob;
    try {
      blob = daB64(b.b);
    } catch (e) {
      return;
    }
    const tipo = tipoBlocco(blob);
    const inv = this.s.inviti.find((i) => i.mb === b.mb);
    if (inv) {
      if (tipo === "stretta1") await this.nuovaRichiesta(blob);
      return;
    }
    const k = this.s.contatti.find((x) => x.mia && x.mia.mb === b.mb);
    if (!k || k.bloccato) return;
    if (tipo === "stretta2" && k.stato === "invitato") return this.completaStretta(k, blob);
    if (tipo === "messaggio" && (k.stato === "attivo" || k.stato === "accettato")) return this.nuovoMessaggio(k, blob);
  }
  async nuovaRichiesta(blob) {
    if (this.s.contatti.length >= MAX_CONTATTI) return;
    let r;
    try {
      r = riceviStretta1(this.identita, blob);
    } catch (e) {
      return;
    }
    const ikHex = inHex(r.ikI);
    if (ikHex === this.s.ident.pub) return;
    if (this.s.bloccati.some((x) => x.ik === ikHex)) return;
    const d = r.dati;
    if (!esadecimale(d.mb, 32) || !esadecimale(d.tk, 64)) return;
    if (this.s.contatti.some((k2) => k2.ikRemota === ikHex && k2.stato !== "rifiutato")) return;
    const k = {
      id: nuovoId(),
      nome: pulisciNome(d.n) || "Sconosciuto",
      alias: "",
      stato: "richiesta",
      ikRemota: ikHex,
      mia: null,
      verso: { mb: d.mb, tk: d.tk },
      stretta: null,
      rstato: r.stato,
      sessione: null,
      verificato: false,
      visti: {},
      nonLetti: 0,
      creato: this.ora()
    };
    this.s.contatti.push(k);
    this.emetti("richiesta", k);
    this.emetti("contatto", k);
  }
  async completaStretta(k, blob) {
    let r;
    try {
      r = finisciStretta(this.identita, k.stretta, blob);
    } catch (e) {
      return;
    }
    const d = r.dati;
    if (!esadecimale(d.mb, 32) || !esadecimale(d.tk, 64)) return;
    k.sessione = r.sessione;
    k.verso = { mb: d.mb, tk: d.tk };
    k.nome = pulisciNome(d.n) || k.nome;
    k.stretta = null;
    k.stato = "attivo";
    this.s.coda = this.s.coda.filter((q) => !(q.cid === k.id && q.tipo === "stretta"));
    this.accodaMessaggio(k, { t: "k", id: nuovoId() }, "k");
    this.emetti("contatto", k);
  }
  async nuovoMessaggio(k, blob) {
    var _a, _b;
    const h = shaHex(blob).slice(0, 32);
    let testo;
    try {
      if (!k.sessione) {
        const r = accettaPrimoMessaggio(k.rstato, blob);
        k.sessione = r.sessione;
        k.rstato = null;
        k.stato = "attivo";
        this.s.coda = this.s.coda.filter((q) => !(q.cid === k.id && q.tipo === "stretta2"));
        testo = r.testo;
        this.emetti("contatto", k);
      } else {
        testo = decifra(k.sessione, blob);
      }
    } catch (e) {
      const idNoto = k.visti[h];
      if (idNoto && k.sessione && k.sessione.cks) {
        this.accodaMessaggio(k, { t: "a", id: idNoto }, "ack");
      }
      return;
    }
    let o;
    try {
      o = byteATesto(testo);
    } catch (e) {
      return;
    }
    k.visti[h] = typeof o.id === "string" ? o.id : "";
    const chiavi = Object.keys(k.visti);
    if (chiavi.length > MAX_VISTI) for (const x of chiavi.slice(0, chiavi.length - MAX_VISTI)) delete k.visti[x];
    if (o.t === "m" && typeof o.x === "string" && esadecimale(o.id, 16)) {
      const lista = (_a = this.messaggi)[_b = k.id] || (_a[_b] = []);
      if (!lista.some((m) => m.id === o.id)) {
        const m = { id: o.id, dir: "in", ts: this.ora(), x: o.x.slice(0, MASSIMO_TESTO), st: "consegnato" };
        lista.push(m);
        k.nonLetti = (k.nonLetti || 0) + 1;
        await this._salvaMessaggi(k.id, lista);
        this.emetti("messaggio", { cid: k.id, m });
      }
      this.accodaMessaggio(k, { t: "a", id: o.id }, "ack");
    } else if (o.t === "k" && esadecimale(o.id, 16)) {
      this.accodaMessaggio(k, { t: "a", id: o.id }, "ack");
    } else if (o.t === "a" && esadecimale(o.id, 16)) {
      const m = (this.messaggi[k.id] || []).find((x) => x.id === o.id);
      if (m && m.st !== "consegnato") {
        m.st = "consegnato";
        await this._salvaMessaggi(k.id, this.messaggi[k.id]);
        this.emetti("messaggio", { cid: k.id, m });
      }
      this.s.coda = this.s.coda.filter((q) => !(q.cid === k.id && q.mid === o.id));
    }
  }
  // ---------------------------------------------------------------- ciclo
  /** Resta collegato: ascolta e consegna finché non si chiama ferma(). */
  async avvia(attesa = 15) {
    this.attivo = true;
    let errori = 0;
    while (this.attivo) {
      try {
        await this.inviaCoda();
        const prossima = this.s.coda.reduce((m, q) => Math.min(m, q.prossimo), Infinity);
        const secondi = Math.max(1, Math.min(attesa, Math.ceil((prossima - this.ora()) / 1e3)));
        const n = await this.ascolta(secondi);
        if (!this.collegatoNotificato) {
          this.collegatoNotificato = true;
          this.emetti("collegamento", true);
        }
        errori = 0;
        if (!n && !this.cassette().length) await new Promise((r) => setTimeout(r, 1e3));
      } catch (e) {
        errori++;
        if (this.collegatoNotificato !== false) {
          this.collegatoNotificato = false;
          this.emetti("collegamento", false);
        }
        await new Promise((r) => setTimeout(r, Math.min(3e4, 2e3 * errori)));
      }
    }
  }
  ferma() {
    this.attivo = false;
  }
};

// archivio.js
var te2 = new TextEncoder();
var td2 = new TextDecoder();
function nomeDb(nome) {
  return "wgchat-" + nome.toLowerCase().replace(/[^a-z0-9]/g, "_").slice(0, 24) + "-" + nome.length;
}
function db(nome) {
  return new Promise((ok, no) => {
    const r = indexedDB.open(nomeDb(nome), 1);
    r.onupgradeneeded = () => {
      r.result.createObjectStore("dati");
    };
    r.onsuccess = () => ok(r.result);
    r.onerror = () => no(r.error);
  });
}
function richiesta(r) {
  return new Promise((ok, no) => {
    r.onsuccess = () => ok(r.result);
    r.onerror = () => no(r.error);
  });
}
async function conStore(nome, modo, f) {
  const d = await db(nome);
  try {
    const t = d.transaction("dati", modo);
    const risultato = await f(t.objectStore("dati"));
    await new Promise((ok, no) => {
      t.oncomplete = ok;
      t.onerror = () => no(t.error);
      t.onabort = () => no(t.error);
    });
    return risultato;
  } finally {
    d.close();
  }
}
async function deriva(nome, password) {
  const base = await crypto.subtle.importKey("raw", te2.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bit = new Uint8Array(await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt: te2.encode("WGAccount-v1|" + nome.toLowerCase()), iterations: 6e5 }, base, 512));
  const auth = Array.from(bit.slice(0, 32), (x) => x.toString(16).padStart(2, "0")).join("");
  return { auth, master: bit.slice(32) };
}
async function cifra2(chiave, nome, testo) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const c = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv, additionalData: te2.encode(nome) }, chiave, te2.encode(testo)));
  const r = new Uint8Array(12 + c.length);
  r.set(iv);
  r.set(c, 12);
  return r;
}
async function decifra2(chiave, nome, dati) {
  const d = new Uint8Array(dati);
  const p = await crypto.subtle.decrypt({ name: "AES-GCM", iv: d.slice(0, 12), additionalData: te2.encode(nome) }, chiave, d.slice(12));
  return td2.decode(p);
}
async function esiste(utente) {
  try {
    return !!await conStore(utente, "readonly", (s) => richiesta(s.get("stato")));
  } catch (e) {
    return false;
  }
}
var Archivio = class {
  constructor(utente, chiave, stato, messaggi) {
    this.utente = utente;
    this.chiave = chiave;
    this.stato = stato;
    this.messaggi = messaggi;
  }
  async salvaStato(stato) {
    const c = await cifra2(this.chiave, "stato", JSON.stringify(stato));
    await conStore(this.utente, "readwrite", (s) => richiesta(s.put(c, "stato")));
  }
  async salvaMessaggi(cid, elenco) {
    if (elenco === null) {
      await conStore(this.utente, "readwrite", (s) => richiesta(s.delete("m:" + cid)));
      return;
    }
    const c = await cifra2(this.chiave, "m:" + cid, JSON.stringify(elenco));
    await conStore(this.utente, "readwrite", (s) => richiesta(s.put(c, "m:" + cid)));
  }
};
function chiaveDa(master) {
  return crypto.subtle.importKey("raw", master, "AES-GCM", false, ["encrypt", "decrypt"]);
}
function chiaveDaMaster(master) {
  return chiaveDa(master);
}
async function crea(utente, master, stato) {
  const a = new Archivio(utente, master instanceof CryptoKey ? master : await chiaveDa(master), stato, {});
  await a.salvaStato(stato);
  return a;
}
async function apri(utente, master) {
  const chiave = master instanceof CryptoKey ? master : await chiaveDa(master);
  let stato;
  try {
    const c = await conStore(utente, "readonly", (s) => richiesta(s.get("stato")));
    stato = JSON.parse(await decifra2(chiave, "stato", c));
  } catch (e) {
    return null;
  }
  const messaggi = {};
  const chiavi = await conStore(utente, "readonly", (s) => richiesta(s.getAllKeys()));
  for (const k of chiavi) {
    if (typeof k !== "string" || !k.startsWith("m:")) continue;
    try {
      const c = await conStore(utente, "readonly", (s) => richiesta(s.get(k)));
      messaggi[k.slice(2)] = JSON.parse(await decifra2(chiave, k, c));
    } catch (e) {
    }
  }
  return new Archivio(utente, chiave, stato, messaggi);
}
async function cancellaTutto(utente) {
  await new Promise((ok, no) => {
    const r = indexedDB.deleteDatabase(nomeDb(utente));
    r.onsuccess = ok;
    r.onerror = () => no(r.error);
    r.onblocked = ok;
  });
}
var DB_RICORDO = "wgchat-ricordo";
function dbRicordo() {
  return new Promise((ok, no) => {
    const r = indexedDB.open(DB_RICORDO, 2);
    r.onupgradeneeded = () => {
      if (!r.result.objectStoreNames.contains("r")) r.result.createObjectStore("r");
    };
    r.onsuccess = () => ok(r.result);
    r.onerror = () => no(r.error);
  });
}
async function chiaveRicordo(d) {
  let k = await richiesta(d.transaction("r", "readonly").objectStore("r").get("chiave"));
  if (k) return k;
  k = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
  const t = d.transaction("r", "readwrite");
  t.objectStore("r").put(k, "chiave");
  await new Promise((ok, no) => {
    t.oncomplete = ok;
    t.onerror = () => no(t.error);
  });
  return k;
}
async function ricorda(utente, password) {
  const d = await dbRicordo();
  try {
    const chiave = await chiaveRicordo(d);
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const c = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, chiave, te2.encode(password)));
    const t = d.transaction("r", "readwrite");
    t.objectStore("r").put({ utente, iv, c }, "ricordo");
    await new Promise((ok, no) => {
      t.oncomplete = ok;
      t.onerror = () => no(t.error);
    });
  } finally {
    d.close();
  }
}
async function ricordo() {
  try {
    const d = await dbRicordo();
    try {
      const salvato = await richiesta(d.transaction("r", "readonly").objectStore("r").get("ricordo"));
      if (!salvato || !salvato.utente || !salvato.c) return null;
      const chiave = await richiesta(d.transaction("r", "readonly").objectStore("r").get("chiave"));
      if (!chiave) return null;
      const chiaro = await crypto.subtle.decrypt({ name: "AES-GCM", iv: salvato.iv }, chiave, salvato.c);
      return { utente: salvato.utente, password: td2.decode(chiaro) };
    } finally {
      d.close();
    }
  } catch (e) {
    return null;
  }
}
async function dimentica() {
  await new Promise((ok) => {
    const r = indexedDB.deleteDatabase(DB_RICORDO);
    r.onsuccess = ok;
    r.onerror = ok;
    r.onblocked = ok;
  });
}

// prova.js
var K256 = new Uint32Array([
  1116352408,
  1899447441,
  3049323471,
  3921009573,
  961987163,
  1508970993,
  2453635748,
  2870763221,
  3624381080,
  310598401,
  607225278,
  1426881987,
  1925078388,
  2162078206,
  2614888103,
  3248222580,
  3835390401,
  4022224774,
  264347078,
  604807628,
  770255983,
  1249150122,
  1555081692,
  1996064986,
  2554220882,
  2821834349,
  2952996808,
  3210313671,
  3336571891,
  3584528711,
  113926993,
  338241895,
  666307205,
  773529912,
  1294757372,
  1396182291,
  1695183700,
  1986661051,
  2177026350,
  2456956037,
  2730485921,
  2820302411,
  3259730800,
  3345764771,
  3516065817,
  3600352804,
  4094571909,
  275423344,
  430227734,
  506948616,
  659060556,
  883997877,
  958139571,
  1322822218,
  1537002063,
  1747873779,
  1955562222,
  2024104815,
  2227730452,
  2361852424,
  2428436474,
  2756734187,
  3204031479,
  3329325298
]);
var W256 = new Uint32Array(64);
var M256 = new Uint8Array(256);
function sha256Blocchi(bytes, lunghezza, fuori) {
  let h0 = 1779033703, h1 = 3144134277, h2 = 1013904242, h3 = 2773480762, h4 = 1359893119, h5 = 2600822924, h6 = 528734635, h7 = 1541459225;
  const totale = lunghezza + 9 + 63 >> 6 << 6;
  if (totale > M256.length) throw new Error("Messaggio troppo lungo per la prova.");
  const m = M256;
  m.fill(0, 0, totale);
  m.set(bytes.subarray(0, lunghezza));
  m[lunghezza] = 128;
  const bit = lunghezza * 8;
  m[totale - 4] = bit >>> 24 & 255;
  m[totale - 3] = bit >>> 16 & 255;
  m[totale - 2] = bit >>> 8 & 255;
  m[totale - 1] = bit & 255;
  for (let o = 0; o < totale; o += 64) {
    for (let i = 0; i < 16; i++) W256[i] = m[o + 4 * i] << 24 | m[o + 4 * i + 1] << 16 | m[o + 4 * i + 2] << 8 | m[o + 4 * i + 3];
    for (let i = 16; i < 64; i++) {
      const a2 = W256[i - 15], b2 = W256[i - 2];
      const s0 = (a2 >>> 7 | a2 << 25) ^ (a2 >>> 18 | a2 << 14) ^ a2 >>> 3;
      const s1 = (b2 >>> 17 | b2 << 15) ^ (b2 >>> 19 | b2 << 13) ^ b2 >>> 10;
      W256[i] = W256[i - 16] + s0 + W256[i - 7] + s1 | 0;
    }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (let i = 0; i < 64; i++) {
      const S1 = (e >>> 6 | e << 26) ^ (e >>> 11 | e << 21) ^ (e >>> 25 | e << 7);
      const t1 = h + S1 + (e & f ^ ~e & g) + K256[i] + W256[i] | 0;
      const S0 = (a >>> 2 | a << 30) ^ (a >>> 13 | a << 19) ^ (a >>> 22 | a << 10);
      const t2 = S0 + (a & b ^ a & c ^ b & c) | 0;
      h = g;
      g = f;
      f = e;
      e = d + t1 | 0;
      d = c;
      c = b;
      b = a;
      a = t1 + t2 | 0;
    }
    h0 = h0 + a | 0;
    h1 = h1 + b | 0;
    h2 = h2 + c | 0;
    h3 = h3 + d | 0;
    h4 = h4 + e | 0;
    h5 = h5 + f | 0;
    h6 = h6 + g | 0;
    h7 = h7 + h | 0;
  }
  fuori[0] = h0;
  fuori[1] = h1;
  fuori[2] = h2;
  fuori[3] = h3;
  fuori[4] = h4;
  fuori[5] = h5;
  fuori[6] = h6;
  fuori[7] = h7;
}
function zeriInizialiNumero(n) {
  return n === 0 ? 32 : Math.clz32(n);
}
async function risolviProva(sfida, utente, bit, avanza) {
  const te3 = new TextEncoder();
  const prefisso = te3.encode(sfida + "|" + utente.toLowerCase() + "|");
  const buffer = new Uint8Array(prefisso.length + 16);
  buffer.set(prefisso);
  const fuori = new Int32Array(8);
  const attesi = Math.pow(2, bit);
  let contatore = 0;
  for (; ; ) {
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
    if (avanza) avanza(Math.min(99, Math.round(contatore / attesi * 100)));
    await new Promise((ok) => setTimeout(ok, 0));
  }
}

// app.js
var API_ACCOUNT = location.hostname.indexOf("chat.") === 0 ? "https://" + location.hostname.slice(5) + "/giochi/giochi.php" : "../giochi/giochi.php";
var URL_GIOCHI = location.hostname.indexOf("chat.") === 0 ? "https://" + location.hostname.slice(5) + "/giochi/" : "../giochi/";
var $ = (id) => document.getElementById(id);
function el(tag, attrs, ...figli) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (k === "testo") e.textContent = v;
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else e.setAttribute(k, v);
  }
  for (const f of figli.flat()) if (f !== null && f !== void 0) e.append(f);
  return e;
}
var motore = null;
var token = "";
var nomeUtente = "";
var corrente = "";
var titoloBase = document.title;
var ATTESA = Math.max(1, Math.min(20, Number(new URLSearchParams(location.search).get("attesa")) || 15));
async function account(azione, corpo, metodo = "POST") {
  const r = await fetch(API_ACCOUNT + "?a=" + azione, {
    method: metodo,
    headers: Object.assign({ "Content-Type": "application/json" }, token ? { "X-Token": token } : {}),
    body: metodo === "POST" ? JSON.stringify(corpo || {}) : void 0,
    cache: "no-store",
    credentials: "omit",
    referrerPolicy: "no-referrer"
  });
  const dati = await r.json().catch(() => ({}));
  if (!r.ok) {
    const e = new Error(dati.errore || "Errore del server.");
    e.codice = r.status;
    if (dati.prova) e.prova = dati.prova;
    throw e;
  }
  return dati;
}
async function post(azione, corpo) {
  const r = await fetch("chat.php?a=" + azione, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(corpo), cache: "no-store", credentials: "omit", referrerPolicy: "no-referrer" });
  return { codice: r.status, dati: await r.json().catch(() => ({})) };
}
var flussoChat = null;
var flussiChatSospesiFino = 0;
function nuovoFlussoChat(chiave, cassette) {
  const f = {
    chiave,
    ctrl: null,
    blocchi: /* @__PURE__ */ new Map(),
    errore: null,
    fermato: false,
    ultimoUso: Date.now(),
    risvegli: [],
    sveglia() {
      const r = this.risvegli;
      this.risvegli = [];
      r.forEach((x) => x());
    },
    ferma() {
      this.fermato = true;
      try {
        if (this.ctrl) this.ctrl.abort();
      } catch (e) {
      }
      this.sveglia();
    }
  };
  const trattaBlocco = (blocco) => {
    let nome = "message";
    const dati = [];
    for (const riga of blocco.split("\n")) {
      if (riga.startsWith(":")) continue;
      if (riga.startsWith("event:")) nome = riga.slice(6).trim();
      else if (riga.startsWith("data:")) dati.push(riga.slice(5).replace(/^ /, ""));
    }
    if (nome === "errore") {
      let codice = 500;
      try {
        codice = JSON.parse(dati.join("\n")).stato || 500;
      } catch (e2) {
      }
      const e = new Error("Il server ha chiuso il collegamento.");
      e.codice = codice;
      throw e;
    }
    if (nome !== "blocchi" || dati.length === 0) return;
    const d = JSON.parse(dati.join("\n"));
    let nuovi = false;
    for (const b of d.blocchi || []) {
      f.blocchi.set(b.id, b);
      nuovi = true;
    }
    if (nuovi) f.sveglia();
  };
  (async () => {
    let tentativo = 0;
    let inizioCaduta = 0;
    try {
      while (!f.fermato) {
        if (Date.now() - f.ultimoUso > 6e4) break;
        const ctrl = new AbortController();
        f.ctrl = ctrl;
        let orologio = null;
        try {
          const risposta = await fetch("chat.php?a=flusso", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ cassette }),
            cache: "no-store",
            credentials: "omit",
            referrerPolicy: "no-referrer",
            signal: ctrl.signal
          });
          if (!risposta.ok || !(risposta.headers.get("content-type") || "").includes("text/event-stream")) {
            const e = new Error("Flusso non disponibile.");
            e.codice = risposta.ok ? 404 : risposta.status;
            throw e;
          }
          tentativo = 0;
          let ultimoByte = Date.now();
          orologio = setInterval(() => {
            if (Date.now() - ultimoByte > 45e3) ctrl.abort();
          }, 5e3);
          const lettore = risposta.body.getReader();
          const dec = new TextDecoder();
          let buf = "";
          for (; ; ) {
            const { done, value } = await lettore.read();
            if (done) break;
            ultimoByte = Date.now();
            buf += dec.decode(value, { stream: true }).replace(/\r/g, "");
            let i;
            while ((i = buf.indexOf("\n\n")) >= 0) {
              const blocco = buf.slice(0, i);
              buf = buf.slice(i + 2);
              trattaBlocco(blocco);
            }
            if (Date.now() - f.ultimoUso > 6e4) {
              ctrl.abort();
              break;
            }
          }
        } catch (err) {
          if (f.fermato) return;
          if (err && err.codice) {
            f.errore = err;
            if (err.codice === 400 || err.codice === 404 || err.codice >= 500) flussiChatSospesiFino = Date.now() + 3e5;
            return;
          }
          if (Date.now() - f.ultimoUso > 6e4) return;
          tentativo++;
          if (tentativo === 1) inizioCaduta = Date.now();
          if (Date.now() - inizioCaduta > 12e4) {
            const e = new Error("Non riesco a collegarmi al server della chat.");
            e.codice = 0;
            f.errore = e;
            return;
          }
          await new Promise((r) => setTimeout(r, Math.min(15e3, 1e3 * 2 ** Math.min(tentativo - 1, 4)) + Math.random() * 500));
        } finally {
          if (orologio) clearInterval(orologio);
        }
      }
    } finally {
      f.fermato = true;
      f.sveglia();
    }
  })();
  return f;
}
async function ascoltaConFlusso(cassette, ack, attesa) {
  if (attesa > 0 && cassette.length > 0 && Date.now() >= flussiChatSospesiFino && typeof ReadableStream === "function" && typeof AbortController === "function") {
    const chiave = cassette.map((c) => c.mb).sort().join(",");
    if (ack.length > 0) {
      if (flussoChat) for (const id of ack) flussoChat.blocchi.delete(id);
      attesa = 0;
    } else {
      if (!flussoChat || flussoChat.chiave !== chiave || flussoChat.fermato || flussoChat.errore) {
        if (flussoChat) flussoChat.ferma();
        flussoChat = nuovoFlussoChat(chiave, cassette);
      }
      const f = flussoChat;
      f.ultimoUso = Date.now();
      const fine = Date.now() + attesa * 1e3;
      for (; ; ) {
        if (f.blocchi.size > 0) {
          const r2 = [...f.blocchi.values()];
          f.blocchi.clear();
          return r2;
        }
        if (f.errore) {
          if (f.errore.codice === 0) throw f.errore;
          break;
        }
        if (f.fermato) break;
        const resto = fine - Date.now();
        if (resto <= 0) return [];
        await Promise.race([new Promise((ok) => f.risvegli.push(ok)), new Promise((ok) => setTimeout(ok, resto))]);
        f.ultimoUso = Date.now();
      }
    }
  }
  let r = await post("ascolta", { cassette, ack, attesa });
  if (r.codice === 428 && r.dati.prova && r.dati.prova.sfida && r.dati.prova.bit >= 1 && r.dati.prova.bit <= 26) {
    const nonce = await risolviProva(r.dati.prova.sfida, await improntaCassette(cassette), r.dati.prova.bit);
    r = await post("ascolta", { cassette, ack, attesa: 0, prova: { sfida: r.dati.prova.sfida, nonce } });
  }
  if (r.codice !== 200) throw new Error("errore " + r.codice);
  return r.dati.blocchi;
}
async function improntaCassette(cassette) {
  const numeri = [...new Set(cassette.map((c) => String(c.mb).toLowerCase()))].sort();
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(numeri.join(",")));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}
var trasporto = {
  async invia(mb, tk, b) {
    const r = await post("invia", { mb, tk, b });
    return r.codice === 200 ? "ok" : "no";
  },
  async ascolta(cassette, ack, attesa) {
    return ascoltaConFlusso(cassette, ack, attesa);
  }
};
function annuncia(testo) {
  const z = $("annunci");
  z.textContent = "";
  setTimeout(() => {
    z.textContent = testo;
  }, 50);
}
function mostraToast(testo, cid) {
  const box = $("toast");
  if (!box || !testo) return;
  const t = el("button", { type: "button", class: "toast-voce", testo });
  t.addEventListener("click", () => {
    if (cid) apri2(cid);
    t.remove();
  });
  box.append(t);
  setTimeout(() => t.remove(), 6e3);
}
function suoniAttivi() {
  try {
    return localStorage.getItem("wg_suoni") !== "0";
  } catch (e) {
    return true;
  }
}
var audioNotifiche = {};
function suona(nomeSuono) {
  if (!suoniAttivi()) return;
  try {
    const a = audioNotifiche[nomeSuono] || (audioNotifiche[nomeSuono] = new Audio("suoni/" + nomeSuono + ".wav"));
    a.currentTime = 0;
    const p = a.play();
    if (p && p.catch) p.catch(() => {
    });
  } catch (e) {
  }
}
function aggiornaPulsanteSuoni() {
  const b = $("suoni-chat");
  if (!b) return;
  const attivi = suoniAttivi();
  b.textContent = "Suoni: " + (attivi ? "attivi" : "spenti");
  b.setAttribute("aria-pressed", attivi ? "true" : "false");
}
function notifica(titolo, testo, cid) {
  const corpo = testo ? titolo + ": " + testo : titolo;
  if (document.hidden || cid && cid !== corrente) mostraToast(corpo, cid);
  if (typeof Notification === "undefined" || Notification.permission !== "granted") return;
  if (!document.hidden && (!cid || cid === corrente)) return;
  try {
    const n = new Notification(titolo, { body: testo || "", tag: cid || "wg-chat" });
    n.onclick = () => {
      window.focus();
      if (cid) apri2(cid);
      n.close();
    };
  } catch (e) {
  }
}
function aggiornaPulsanteNotifiche() {
  const b = $("attiva-notifiche");
  if (!b) return;
  b.hidden = typeof Notification === "undefined" || Notification.permission === "granted";
}
function aggiornaVista() {
  const aperta = !!corrente && !$("pannello-conversazione").hidden;
  document.body.classList.toggle("conversazione-aperta", aperta);
  const torna = $("torna-contatti");
  if (torna) torna.hidden = !aperta || !window.matchMedia("(max-width: 860px)").matches;
}
function mostra(id) {
  for (const s of ["schermata-sblocco", "schermata-chat"]) $(s).hidden = s !== id;
  aggiornaVista();
}
var modoCrea = false;
function impostaModo(crea2) {
  modoCrea = crea2;
  $("scheda-entra").setAttribute("aria-pressed", crea2 ? "false" : "true");
  $("scheda-crea").setAttribute("aria-pressed", crea2 ? "true" : "false");
  $("riga-ripeti").hidden = !crea2;
  $("campo-ripeti").required = crea2;
  $("campo-password").autocomplete = crea2 ? "new-password" : "current-password";
  $("invio-accesso").textContent = crea2 ? "Registrati" : "Accedi";
  $("errore-sblocco").textContent = "";
}
$("scheda-entra").addEventListener("click", () => impostaModo(false));
$("scheda-crea").addEventListener("click", () => impostaModo(true));
async function iniziale() {
  $("vai-giochi").setAttribute("href", URL_GIOCHI);
  mostra("schermata-sblocco");
  impostaModo(false);
  const salvato = await ricordo();
  if (salvato && salvato.utente && salvato.password) {
    try {
      await entraEApri(salvato.utente, salvato.password, false);
      return;
    } catch (e) {
      if (e.codice === 401) await dimentica();
      else if (e.archivio) {
        $("campo-nome").value = salvato.utente;
        $("errore-sblocco").textContent = e.message;
        $("campo-password").focus();
        return;
      }
    }
  }
  const noto = salvato && salvato.utente || sessionStorage.getItem("wg_nome") || "";
  $("campo-nome").value = noto;
  (noto ? $("campo-password") : $("campo-nome")).focus();
}
async function entraEApri(nome, password, crea2, chiediSalvataggio) {
  const { auth, master } = await deriva(nome, password);
  let r;
  try {
    r = await account(crea2 ? "registra" : "entra", { nome, auth });
  } catch (err) {
    if (!crea2 && err.codice === 401) r = await account("entra", { nome, password });
    else if (crea2 && err.codice === 428 && err.prova && err.prova.sfida && err.prova.bit >= 1 && err.prova.bit <= 26) {
      $("errore-sblocco").textContent = "Un momento: preparo una piccola verifica per creare il nome utente.";
      const nonce = await risolviProva(err.prova.sfida, nome, err.prova.bit);
      $("errore-sblocco").textContent = "";
      r = await account("registra", { nome, auth, prova: { sfida: err.prova.sfida, nonce } });
    } else throw err;
  }
  token = r.token;
  nomeUtente = r.nome;
  sessionStorage.setItem("wg_token", token);
  sessionStorage.setItem("wg_nome", nomeUtente);
  const chiave = await chiaveDaMaster(master);
  let a;
  if (await esiste(nomeUtente)) {
    a = await apri(nomeUtente, chiave);
    if (!a) {
      const e = new Error("La chat non si apre con questa password. Puoi cancellarla e ricominciare.");
      e.archivio = true;
      throw e;
    }
  } else {
    a = await crea(nomeUtente, chiave, statoVuoto(nomeUtente));
  }
  if (chiediSalvataggio) {
    if (confirm("Vuoi ricordare la password su questo telefono o computer?")) {
      await ricorda(nomeUtente, password);
    } else {
      await dimentica();
    }
  }
  $("campo-password").value = "";
  $("campo-ripeti").value = "";
  await avviaChat(a);
}
$("modulo-accesso").addEventListener("submit", async (e) => {
  e.preventDefault();
  const errore = $("errore-sblocco");
  errore.textContent = "";
  const nome = $("campo-nome").value.trim(), password = $("campo-password").value;
  if (password.length < 8) {
    errore.textContent = "La password è troppo corta: servono almeno 8 caratteri.";
    return;
  }
  if (modoCrea && password !== $("campo-ripeti").value) {
    errore.textContent = "Le due password sono diverse.";
    return;
  }
  $("invio-accesso").disabled = true;
  try {
    await entraEApri(nome, password, modoCrea, true);
  } catch (err) {
    errore.textContent = err.message;
  } finally {
    $("invio-accesso").disabled = false;
  }
});
$("cancella-tutto").addEventListener("click", async () => {
  const nome = $("campo-nome").value.trim();
  if (!nome) {
    $("errore-sblocco").textContent = "Scrivi il nome utente.";
    return;
  }
  if (!confirm("Cancellare la chat di " + nome + " su questo telefono o computer?")) return;
  await cancellaTutto(nome);
  $("errore-sblocco").textContent = "Chat cancellata.";
});
async function avviaChat(a) {
  motore = new MotoreChat({
    stato: a.stato,
    messaggi: a.messaggi,
    trasporto,
    nome: nomeUtente,
    salvaStato: (s) => a.salvaStato(s),
    salvaMessaggi: (cid, elenco) => a.salvaMessaggi(cid, elenco)
  });
  motore.su("messaggio", ({ cid, m }) => {
    const k = motore.contatto(cid);
    if (m.dir === "in") {
      const chi = motore.nomeVisibile(k);
      if (cid === corrente && !document.hidden) {
        motore.segnaLetti(cid);
        annuncia(chi + ": " + m.x);
      } else {
        annuncia("Nuovo messaggio da " + chi);
        suona("notifica");
        notifica(chi, m.x, cid);
      }
    }
    disegnaElenco();
    if (cid === corrente) disegnaConversazione();
    aggiornaTitolo();
  });
  motore.su("richiesta", (k) => {
    annuncia(k.nome + " vuole scriverti.");
    suona("richiesta");
    notifica(k.nome, "Vuole scriverti", k.id);
    disegnaElenco();
    aggiornaTitolo();
  });
  motore.su("contatto", () => {
    disegnaElenco();
    disegnaBloccati();
    if (corrente) disegnaConversazione();
  });
  motore.su("collegamento", (v) => {
    $("stato-collegamento").textContent = v ? "Collegato" : "Non collegato: riprovo";
  });
  mostra("schermata-chat");
  aggiornaPulsanteNotifiche();
  $("mio-nome").textContent = nomeUtente;
  $("trovabile").checked = motore.s.trovabile !== false;
  disegnaElenco();
  disegnaBloccati();
  await aggiornaPubblicazione();
  motore.avvia(ATTESA);
  const dest = decodeURIComponent((location.hash.match(/con=([^&]+)/) || [])[1] || "");
  if (dest) {
    history.replaceState(null, "", location.pathname);
    await contattaPerNome(dest);
  }
  $("nuovo-nome-casella").focus();
}
function aggiornaTitolo() {
  const n = motore.s.contatti.reduce((t, k) => t + (k.nonLetti || 0) + (k.stato === "richiesta" ? 1 : 0), 0);
  document.title = (n ? "(" + n + ") " : "") + titoloBase;
}
async function aggiornaPubblicazione() {
  try {
    if (motore.s.trovabile === false) {
      await account("chat_ritira");
      $("mio-codice-riga").hidden = true;
      return;
    }
    if (!motore.s.inviti.length) await motore.nuovoInvito();
    const inv = motore.s.inviti[motore.s.inviti.length - 1];
    $("mio-codice").value = inv.codice;
    $("mio-codice-riga").hidden = false;
    await account("chat_pubblica", { codice: inv.codice });
  } catch (e) {
    annuncia("Non riesco a farmi trovare: " + e.message);
  }
}
$("trovabile").addEventListener("change", async () => {
  motore.s.trovabile = $("trovabile").checked;
  await motore.salva();
  await aggiornaPubblicazione();
  annuncia($("trovabile").checked ? "Chi cerca il tuo nome può scriverti." : "Nessuno ti trova più per nome.");
});
$("crea-codice").addEventListener("click", async () => {
  await motore.nuovoInvito();
  await aggiornaPubblicazione();
  annuncia("Codice nuovo. Il precedente non vale più.");
});
$("copia-codice").addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText($("mio-codice").value);
    annuncia("Codice copiato.");
  } catch (e) {
    $("mio-codice").select();
    annuncia("Seleziona il codice e copialo.");
  }
});
$("suoni-chat").addEventListener("click", () => {
  try {
    localStorage.setItem("wg_suoni", suoniAttivi() ? "0" : "1");
  } catch (e) {
  }
  aggiornaPulsanteSuoni();
  annuncia(suoniAttivi() ? "Suoni attivi." : "Suoni spenti.");
});
aggiornaPulsanteSuoni();
$("attiva-notifiche").addEventListener("click", async () => {
  if (typeof Notification === "undefined") return;
  const permesso = await Notification.requestPermission();
  aggiornaPulsanteNotifiche();
  annuncia(permesso === "granted" ? "Notifiche attive." : "Notifiche non attive.");
});
$("torna-contatti").addEventListener("click", () => {
  corrente = "";
  $("pannello-conversazione").hidden = true;
  $("pannello-vuoto").hidden = false;
  aggiornaVista();
  disegnaElenco();
  $("nuovo-nome-casella").focus();
});
window.addEventListener("resize", aggiornaVista);
function descrizioneContatto(k) {
  const n = motore.nomeVisibile(k) + (k.bloccato ? " (bloccato)" : "");
  switch (k.stato) {
    case "richiesta":
      return n + ", vuole scriverti";
    case "invitato":
      return n + ", in attesa";
    case "accettato":
      return n + ", quasi pronto";
    default:
      return n + (k.nonLetti ? ", " + k.nonLetti + (k.nonLetti === 1 ? " messaggio nuovo" : " messaggi nuovi") : "");
  }
}
function disegnaElenco() {
  const ul = $("elenco-contatti");
  ul.replaceChildren();
  $("nessun-contatto").hidden = motore.s.contatti.length > 0;
  const peso = (k) => k.stato === "richiesta" ? 0 : k.nonLetti ? 1 : 2;
  const ordinati = [...motore.s.contatti].sort((a, b) => peso(a) - peso(b) || motore.nomeVisibile(a).localeCompare(motore.nomeVisibile(b)));
  for (const k of ordinati) {
    const b = el("button", { type: "button", class: "contatto" + (k.id === corrente ? " aperto" : "") + (k.nonLetti || k.stato === "richiesta" ? " nuovo" : ""), testo: descrizioneContatto(k), "aria-current": k.id === corrente ? "true" : "false", onclick: () => apri2(k.id) });
    ul.append(el("li", {}, b));
  }
}
function disegnaBloccati() {
  const ul = $("elenco-bloccati");
  ul.replaceChildren();
  const b = motore.elencoBloccati();
  $("nessun-bloccato").hidden = b.length > 0;
  for (const x of b) {
    ul.append(el("li", { class: "riga" }, el("span", { testo: x.nome + (x.utente ? "" : " (senza nome utente)") }), el("button", { type: "button", class: "secondario", testo: "Sblocca", "aria-label": "Sblocca " + x.nome, onclick: () => sblocca(x) })));
  }
}
async function sblocca(x) {
  const utente = await motore.sblocca(x.ik);
  if (utente) {
    try {
      await account("sblocca", { nome: utente });
    } catch (e) {
    }
  }
  annuncia(x.nome + " è stato sbloccato.");
}
async function bloccaUtenteNome(utente) {
  if (!utente) return;
  try {
    await account("blocca", { nome: utente });
  } catch (e) {
    annuncia("Bloccato qui. Nei giochi non ci sono riuscito.");
  }
}
async function contattaPerNome(nome) {
  $("errore-aggiungi").textContent = "";
  try {
    const gia = motore.s.contatti.find((k2) => (k2.utente || "").toLowerCase() === nome.toLowerCase() && k2.stato !== "rifiutato");
    if (gia) {
      apri2(gia.id);
      return;
    }
    const r = await account("cerca", { nome });
    const k = await motore.aggiungiContatto(r.codice, r.nome);
    annuncia("Richiesta inviata a " + r.nome + ".");
    apri2(k.id);
  } catch (err) {
    $("errore-aggiungi").textContent = err.codice === 404 ? "Non trovo questa persona. Se hai un codice, usalo qui sotto." : err.message;
  }
}
$("modulo-aggiungi-nome").addEventListener("submit", async (e) => {
  e.preventDefault();
  const n = $("nuovo-nome-casella").value.trim();
  if (n) {
    await contattaPerNome(n);
    $("nuovo-nome-casella").value = "";
  }
});
$("modulo-aggiungi").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("errore-aggiungi").textContent = "";
  try {
    await motore.aggiungiContatto($("nuovo-codice-casella").value);
    $("nuovo-codice-casella").value = "";
    annuncia("Richiesta inviata.");
  } catch (err) {
    $("errore-aggiungi").textContent = err.message;
  }
});
$("esci-chat").addEventListener("click", () => {
  motore.ferma();
  sessionStorage.removeItem("wg_token");
  location.reload();
});
$("esci-dimentica").addEventListener("click", async () => {
  motore.ferma();
  sessionStorage.removeItem("wg_token");
  await dimentica();
  location.reload();
});
function apri2(cid) {
  const k = motore.contatto(cid);
  if (!k) return;
  corrente = cid;
  motore.segnaLetti(cid);
  $("pannello-vuoto").hidden = true;
  $("pannello-conversazione").hidden = false;
  disegnaConversazione();
  disegnaElenco();
  aggiornaTitolo();
  aggiornaVista();
  if (k.stato === "attivo" && !k.bloccato) $("testo-messaggio").focus();
  else $("titolo-conversazione").focus();
}
function etichettaStato(m, nome) {
  if (m.dir === "in") return "";
  switch (m.st) {
    case "consegnato":
      return "consegnato";
    case "inviato":
      return "inviato";
    case "scaduto":
      return "non consegnato";
    default:
      return "in attesa";
  }
}
function disegnaConversazione() {
  const k = motore.contatto(corrente);
  if (!k) {
    corrente = "";
    $("pannello-conversazione").hidden = true;
    $("pannello-vuoto").hidden = false;
    return;
  }
  const nome = motore.nomeVisibile(k);
  $("titolo-conversazione").textContent = nome + (k.bloccato ? " (bloccato)" : "");
  $("stato-verifica").textContent = k.verificato ? "Controllato." : "Non ancora controllato.";
  $("numero-sicurezza").textContent = motore.numeroDiSicurezza(corrente);
  $("segna-verificato").hidden = k.verificato;
  $("riquadro-richiesta").hidden = k.stato !== "richiesta";
  $("modulo-scrivi").hidden = k.stato !== "attivo" || !!k.bloccato;
  $("attesa-contatto").hidden = !(k.stato === "invitato" || k.stato === "accettato") || !!k.bloccato;
  $("attesa-contatto").textContent = k.stato === "invitato" ? nome + " non ha ancora accettato." : "In attesa di " + nome + ".";
  $("testo-richiesta").textContent = nome + " vuole scriverti.";
  $("blocca-contatto").hidden = !!k.bloccato;
  $("sblocca-contatto").hidden = !k.bloccato;
  const log = $("messaggi");
  const prima = log.scrollHeight - log.scrollTop - log.clientHeight < 40;
  log.replaceChildren();
  for (const m of motore.elencoMessaggi(corrente)) {
    const ora = new Date(m.ts).toLocaleTimeString("it-IT", { hour: "2-digit", minute: "2-digit" });
    const stato = etichettaStato(m, nome);
    log.append(el(
      "p",
      { class: "msg " + m.dir },
      el("span", { class: "chi", testo: "[" + ora + "] " + (m.dir === "in" ? nome : "Tu") + ": " }),
      el("span", { class: "testo", testo: m.x }),
      stato ? el("span", { class: "stato", testo: " (" + stato + ")" }) : null
    ));
  }
  if (prima) log.scrollTop = log.scrollHeight;
  aggiornaVista();
}
$("modulo-scrivi").addEventListener("submit", async (e) => {
  e.preventDefault();
  const campo = $("testo-messaggio");
  if (!campo.value.trim()) return;
  try {
    await motore.scrivi(corrente, campo.value);
    campo.value = "";
    $("errore-scrivi").textContent = "";
  } catch (err) {
    $("errore-scrivi").textContent = err.message;
  }
});
$("testo-messaggio").addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    $("modulo-scrivi").requestSubmit();
  }
});
$("accetta-richiesta").addEventListener("click", async () => {
  try {
    await motore.accetta(corrente);
    annuncia("Hai accettato.");
  } catch (e) {
    annuncia(e.message);
  }
});
$("rifiuta-richiesta").addEventListener("click", async () => {
  await motore.rifiuta(corrente);
  corrente = "";
  disegnaElenco();
  disegnaConversazione();
});
$("segna-verificato").addEventListener("click", async () => {
  await motore.segnaVerificato(corrente);
  annuncia("Segnato come verificato.");
});
$("blocca-contatto").addEventListener("click", async () => {
  const k = motore.contatto(corrente);
  if (!confirm("Bloccare " + motore.nomeVisibile(k) + "? Non vi scriverete più.")) return;
  const utente = await motore.blocca(corrente);
  await bloccaUtenteNome(utente);
  annuncia(motore.nomeVisibile(k) + " è stato bloccato.");
});
$("sblocca-contatto").addEventListener("click", async () => {
  const k = motore.contatto(corrente);
  await sblocca({ ik: k.ikRemota, nome: motore.nomeVisibile(k) });
});
$("rinomina").addEventListener("click", async () => {
  const k = motore.contatto(corrente);
  const n = prompt("Nome da mostrare", motore.nomeVisibile(k));
  if (n !== null) await motore.rinomina(corrente, n);
});
$("elimina").addEventListener("click", async () => {
  const k = motore.contatto(corrente);
  if (!confirm("Eliminare la chat con " + motore.nomeVisibile(k) + "?")) return;
  await motore.elimina(corrente);
  corrente = "";
  disegnaElenco();
  disegnaConversazione();
});
$("rileggi").addEventListener("click", () => {
  const k = motore.contatto(corrente);
  const ultimi = motore.elencoMessaggi(corrente).slice(-5);
  annuncia(ultimi.length ? ultimi.map((m) => (m.dir === "in" ? motore.nomeVisibile(k) : "Tu") + ": " + m.x).join(". ") : "Non ci sono ancora messaggi.");
});
document.addEventListener("visibilitychange", () => {
  if (!document.hidden && corrente && motore) {
    motore.segnaLetti(corrente);
    aggiornaTitolo();
  }
});
iniziale();
