// Fake WebGL2 and 2D contexts that record resource creation/deletion and call arguments.

const CREATE = /^create([A-Z]\w*)$/;
const DELETE = /^delete([A-Z]\w*)$/;

export function createFakeGL() {
  const live = new Map(); // resource kind -> Set of live handles
  const calls = [];
  let nextId = 1;

  const target = {
    COMPILE_STATUS: 0x8b81,
    LINK_STATUS: 0x8b82,
    getShaderParameter: () => true,
    getProgramParameter: () => true,
    getShaderInfoLog: () => "",
    getProgramInfoLog: () => "",
    getUniformLocation: (_p, name) => ({ uniform: name }),
    getAttribLocation: () => 0,
    getError: () => 0,
    isContextLost: () => false,
  };

  const gl = new Proxy(target, {
    get(t, prop) {
      if (prop in t) return t[prop];
      if (typeof prop !== "string") return undefined;
      if (/^[A-Z0-9_]+$/.test(prop)) return 1; // GL constants
      const create = CREATE.exec(prop);
      const del = DELETE.exec(prop);
      return (...args) => {
        calls.push({ name: prop, args });
        if (create) {
          const handle = { kind: create[1], id: nextId++ };
          if (!live.has(handle.kind)) live.set(handle.kind, new Set());
          live.get(handle.kind).add(handle);
          return handle;
        }
        if (del && args[0]) live.get(del[1])?.delete(args[0]);
        return undefined;
      };
    },
    set(t, prop, value) {
      t[prop] = value;
      return true;
    },
  });

  return {
    gl,
    calls,
    liveResources: () => [...live].flatMap(([kind, set]) => (set.size ? [`${kind} x${set.size}`] : [])),
  };
}

function createFake2D() {
  return new Proxy(
    {},
    {
      get: (t, prop) => (prop in t ? t[prop] : () => undefined),
      set(t, prop, value) {
        t[prop] = value;
        return true;
      },
    },
  );
}

export function createFakeCanvas({ clientWidth = 1920, clientHeight = 1080 } = {}) {
  const fake = createFakeGL();
  const canvas = {
    clientWidth,
    clientHeight,
    width: 300,
    height: 150,
    getContext: (type) => (type === "webgl2" ? fake.gl : type === "2d" ? createFake2D() : null),
  };
  return { canvas, ...fake };
}

export function createAudio({ level = 0 } = {}) {
  const bands = new Float32Array(64).fill(level);
  const waveform = new Float32Array(1024);
  for (let i = 0; i < waveform.length; i++) waveform[i] = level * Math.sin(i / 8);
  return { bands, bass: level, mid: level, treble: level, waveform, rms: level };
}
