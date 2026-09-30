// Aurora Ring: a flowing, audio-reactive colour field with a mirrored radial spectrum.
// Plugin interface (see project.md): init(canvas), frame(audio, time), dispose()
//   audio = { bands: Float32Array(64) in 0..1, bass, mid, treble in 0..1 }

const VERT = `#version 300 es
in vec2 aPos;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); }`;

const FRAG = `#version 300 es
precision highp float;
uniform vec2 uRes;
uniform float uTime;
uniform vec3 uLevels;        // bass, mid, treble
uniform sampler2D uBands;    // 64x1, R8
out vec4 outColor;

float hash(vec2 p) {
  p = fract(p * vec2(123.34, 456.21));
  p += dot(p, p + 45.32);
  return fract(p.x * p.y);
}
float noise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x),
             mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 5; i++) { v += a * noise(p); p = p * 2.03 + 11.7; a *= 0.5; }
  return v;
}
vec3 palette(float t) {
  return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.00, 0.33, 0.67)));
}

void main() {
  vec2 uv = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;
  float t = uTime * 0.10;

  // Flowing background: domain-warped noise, pushed around by the bass.
  vec2 p = uv * 1.7;
  vec2 q = vec2(fbm(p + t), fbm(p + vec2(5.2, 1.3) - t));
  vec2 r = vec2(fbm(p + 3.0 * q + vec2(1.7, 9.2) + t * 1.5 + uLevels.x * 0.5),
                fbm(p + 3.0 * q + vec2(8.3, 2.8) - t));
  float f = fbm(p + 3.5 * r);
  vec3 col = palette(f * 0.8 + t + uLevels.z * 0.25) * (0.15 + f * f * 1.5);
  col *= 0.55 + uLevels.x * 0.9;

  // Radial spectrum: 64 bars mirrored left/right.
  float ang = atan(uv.y, uv.x);
  float a = abs(ang) / 3.14159265;               // 0..1, mirrored
  float cell = a * 64.0;
  float idx = floor(cell);
  float within = fract(cell);
  float b = texture(uBands, vec2((idx + 0.5) / 64.0, 0.5)).r;

  float rad = length(uv);
  float baseR = 0.20 + uLevels.x * 0.045;
  float d = rad - baseR;
  float barLen = 0.012 + b * 0.30;
  float inGap = smoothstep(0.10, 0.22, within) * smoothstep(0.90, 0.78, within);
  float bar = inGap * smoothstep(-0.002, 0.003, d) * smoothstep(barLen + 0.003, barLen - 0.003, d);
  vec3 barCol = palette(a * 0.9 + t * 2.0 + b * 0.3);
  col = mix(col, barCol * (0.8 + b), bar);
  col += barCol * inGap * exp(-abs(d - barLen * 0.5) * 18.0) * b * 0.25;   // soft glow

  // Ring on the bass.
  col += palette(t * 3.0 + 0.5) * exp(-abs(d) * 55.0) * (0.25 + uLevels.x * 0.9);

  // Vignette.
  col *= 1.0 - smoothstep(0.55, 1.15, rad);

  outColor = vec4(pow(col, vec3(0.92)), 1.0);
}`;

function compile(gl, type, src) {
  const s = gl.createShader(type);
  gl.shaderSource(s, src);
  gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
    throw new Error("Shader compile failed: " + gl.getShaderInfoLog(s));
  }
  return s;
}

let gl, prog, vao, buf, tex, canvasRef, loc = {};
const texData = new Uint8Array(64);

export function init(canvas) {
  canvasRef = canvas;
  gl = canvas.getContext("webgl2", { antialias: false, alpha: false });
  if (!gl) throw new Error("WebGL2 not available");

  prog = gl.createProgram();
  gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, VERT));
  gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, FRAG));
  gl.linkProgram(prog);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
    throw new Error("Program link failed: " + gl.getProgramInfoLog(prog));
  }
  gl.useProgram(prog);

  vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  buf = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buf);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW); // one big triangle
  const aPos = gl.getAttribLocation(prog, "aPos");
  gl.enableVertexAttribArray(aPos);
  gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

  tex = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, tex);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, 64, 1, 0, gl.RED, gl.UNSIGNED_BYTE, texData);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

  for (const n of ["uRes", "uTime", "uLevels", "uBands"]) loc[n] = gl.getUniformLocation(prog, n);
  gl.uniform1i(loc.uBands, 0);
}

export function frame(audio, time) {
  const w = canvasRef.clientWidth * Math.min(window.devicePixelRatio || 1, 2);
  const h = canvasRef.clientHeight * Math.min(window.devicePixelRatio || 1, 2);
  if (canvasRef.width !== w || canvasRef.height !== h) {
    canvasRef.width = w;
    canvasRef.height = h;
  }
  gl.viewport(0, 0, w, h);

  for (let i = 0; i < 64; i++) texData[i] = Math.max(0, Math.min(255, audio.bands[i] * 255));
  gl.bindTexture(gl.TEXTURE_2D, tex);
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 64, 1, gl.RED, gl.UNSIGNED_BYTE, texData);

  gl.uniform2f(loc.uRes, w, h);
  gl.uniform1f(loc.uTime, time);
  gl.uniform3f(loc.uLevels, audio.bass, audio.mid, audio.treble);
  gl.drawArrays(gl.TRIANGLES, 0, 3);
}

export function dispose() {
  if (!gl) return;
  gl.deleteTexture(tex);
  gl.deleteBuffer(buf);
  gl.deleteVertexArray(vao);
  gl.deleteProgram(prog);
  gl = null;
}
