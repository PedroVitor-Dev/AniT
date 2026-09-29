const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
let reduceMotion = motionQuery.matches;
motionQuery.addEventListener?.('change', event => { reduceMotion = event.matches; });

const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];

const releaseFallback = 'https://github.com/PedroVitor-Dev/AniT/releases/latest';
const canvas = $('#starfield');
const starContext = canvas?.getContext('2d', { alpha: true });
let stars = [];
let starAnimation;

async function resolveLatestRelease() {
  const links = $$('.release-link');
  const metadata = $$('.release-meta');
  const navVersion = $('.nav-version');
  try {
    const response = await fetch('https://api.github.com/repos/PedroVitor-Dev/AniT/releases/latest', {
      headers: { Accept: 'application/vnd.github+json' }
    });
    if (!response.ok) throw new Error('Release indisponível');
    const release = await response.json();
    const installer = release.assets?.find(asset => /AniT-Setup-.*-win-x64\.exe$/i.test(asset.name));
    const destination = installer?.browser_download_url || release.html_url || releaseFallback;
    const version = String(release.tag_name || '').replace(/^v/i, '') || 'mais recente';
    const size = installer?.size ? `${Math.round(installer.size / 1024 / 1024)} MB` : 'Windows x64';
    links.forEach(link => { link.href = destination; });
    metadata.forEach(item => { item.textContent = `Versão ${version} · ${size}`; });
    if (navVersion) navVersion.textContent = `v${version}`;
  } catch {
    links.forEach(link => { link.href = releaseFallback; });
    metadata.forEach(item => { item.textContent = 'Versão mais recente · Windows x64'; });
  }
}

resolveLatestRelease();

const guide = $('#bakiGuide');
const guideText = $('#guideText');
const guideBaki = $('#guideBaki');
let guideTimer;

function speak(message, mood = 'normal', duration = 5200) {
  if (!guide || !guideText || guide.classList.contains('dismissed')) return;
  const images = {
    normal: './assets/baki-normal.png',
    happy: './assets/baki-happy.png',
    focused: './assets/baki-angry.png',
    sleepy: './assets/baki-sleepy.png'
  };
  guideText.textContent = message;
  if (guideBaki) guideBaki.src = images[mood] || images.normal;
  guide.classList.add('visible');
  window.clearTimeout(guideTimer);
  guideTimer = window.setTimeout(() => guide.classList.remove('visible'), duration);
}

$('#closeGuide')?.addEventListener('click', () => {
  guide?.classList.add('dismissed');
  window.clearTimeout(guideTimer);
});

window.setTimeout(() => speak('Eu cuido das memórias. Você escolhe a próxima história.', 'normal'), 1100);

const atmosphereMessages = {
  madrugada: ['Madrugada acesa. Uma história calma combina com esse céu.', 'sleepy', '#030814'],
  nostalgia: ['Nostalgia escolhida. Até o que passou merece uma luz bonita.', 'normal', '#100b18'],
  maratona: ['Modo maratona. Água por perto e próximo episódio preparado.', 'focused', '#020712']
};

function readPreference(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

function savePreference(key, value) {
  try { localStorage.setItem(key, value); } catch { /* preferências são opcionais */ }
}

function applyAtmosphere(name, announce = false) {
  const safeName = atmosphereMessages[name] ? name : 'madrugada';
  document.documentElement.dataset.atmosphere = safeName;
  $$('.atmosphere-option').forEach(button => {
    const active = button.dataset.atmosphere === safeName;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  const metaTheme = $('meta[name="theme-color"]');
  if (metaTheme) metaTheme.content = atmosphereMessages[safeName][2];
  savePreference('anit-atmosphere', safeName);
  recolorStars();
  if (announce) speak(atmosphereMessages[safeName][0], atmosphereMessages[safeName][1]);
}

applyAtmosphere(readPreference('anit-atmosphere') || 'madrugada');

$$('.atmosphere-option').forEach(button => {
  button.addEventListener('click', () => applyAtmosphere(button.dataset.atmosphere, true));
});

$('.reset-atmosphere')?.addEventListener('click', () => applyAtmosphere('madrugada', true));

const topbar = $('.topbar');
function updateHeader() { topbar?.classList.toggle('scrolled', window.scrollY > 32); }
updateHeader();
window.addEventListener('scroll', updateHeader, { passive: true });

if ('IntersectionObserver' in window) {
  const revealObserver = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.classList.add('visible');
        revealObserver.unobserve(entry.target);
      }
    });
  }, { threshold: .12, rootMargin: '0px 0px -5% 0px' });
  $$('.reveal:not(.visible)').forEach(element => revealObserver.observe(element));
} else {
  $$('.reveal').forEach(element => element.classList.add('visible'));
}

if (!reduceMotion) {
  const heroArchive = $('.hero-archive');
  window.addEventListener('pointermove', event => {
    document.documentElement.style.setProperty('--pointer-x', `${event.clientX}px`);
    document.documentElement.style.setProperty('--pointer-y', `${event.clientY}px`);
    if (!heroArchive || window.innerWidth < 800) return;
    const x = (event.clientX / window.innerWidth - .5) * 14;
    const y = (event.clientY / window.innerHeight - .5) * 9;
    heroArchive.style.setProperty('--archive-x', `${x}px`);
    heroArchive.style.setProperty('--archive-y', `${y}px`);
  }, { passive: true });
}

const machineRoot = $('#machineExperience');
const machineStage = $('#machineStage');
const machineCanvas = $('#machineCanvas');
const machinePoster = $('#machinePoster');
const machineCameraZone = $('#machineCameraZone');
const machineCompat = $('#machineCompat');
const machineFileButtons = $$('[data-machine-file]');
const machineOutput = $('#machineOutput');
const machineSeriesToggle = $('#machineSeriesToggle');
const machineSeriesMeta = $('#machineSeriesMeta');
const machineEpisodes = $('#machineEpisodes');
const machineSelectionCount = $('#machineSelectionCount');
const machineStatus = $('#machineStatus');
const machineBaki = $('#machineBaki');
const machineBakiLine = $('#machineBakiLine');
const organizeMachineButton = $('#organizeMachine');
const machineSelected = new Set();
const machineStepOrder = ['idle', 'intake', 'analysis', 'sorting', 'organized'];
let machineBusy = false;
let machineRunToken = 0;
let machine3D = null;
let machineLoadStarted = false;

function machineMood(mood, line, announce = false) {
  const source = {
    normal: './assets/baki-normal.png',
    happy: './assets/baki-happy.png',
    focused: './assets/baki-angry.png'
  }[mood] || './assets/baki-normal.png';
  if (machineBaki) machineBaki.src = source;
  if (machineBakiLine) machineBakiLine.textContent = line;
  machine3D?.setMood(source);
  if (announce) speak(line, mood, 4300);
}

function setMachineState(state) {
  if (!machineRoot) return;
  machineRoot.dataset.state = state;
  const currentIndex = Math.max(0, machineStepOrder.indexOf(state === 'selected' ? 'idle' : state));
  $$('[data-machine-step]').forEach(step => {
    const index = machineStepOrder.indexOf(step.dataset.machineStep);
    step.classList.toggle('active', index === currentIndex);
    step.classList.toggle('done', index < currentIndex);
  });
  machine3D?.setStage(state);
}

function updateMachineSelection() {
  machineFileButtons.forEach(button => {
    const selected = machineSelected.has(button.dataset.machineFile);
    button.classList.toggle('selected', selected);
    button.setAttribute('aria-pressed', String(selected));
  });
  const count = machineSelected.size;
  if (machineSelectionCount) machineSelectionCount.textContent = `${count} ${count === 1 ? 'fita inserida' : 'fitas inseridas'}`;
  machine3D?.setSelected([...machineSelected]);
  if (!machineBusy && machineRoot?.dataset.state !== 'organized') setMachineState(count ? 'selected' : 'idle');
}

function selectMachineFile(id, announce = true) {
  if (machineBusy || !id) {
    if (machineBusy && machineStatus) machineStatus.textContent = 'A máquina já está processando. Espere só um instante.';
    return;
  }
  if (machineSelected.has(id)) machineSelected.delete(id); else machineSelected.add(id);
  updateMachineSelection();
  const chaotic = machineFileButtons.find(button => button.dataset.machineFile === id)?.dataset.chaotic === 'true';
  if (chaotic && machineSelected.has(id)) {
    const line = 'Espera… isso é um nome ou uma constelação inteira?';
    machineMood('focused', line, announce);
    if (machineStatus) machineStatus.textContent = 'Caos detectado. Procurando 01x04 entre tags técnicas, áudio e hash…';
    window.setTimeout(() => {
      if (!machineBusy && machineSelected.has(id)) {
        machineMood('focused', 'Achei: temporada 01, episódio 04. O resto são marcas técnicas.', false);
        if (machineStatus) machineStatus.textContent = 'Padrão 01x04 reconhecido; WEB, x265, áudio e hash foram separados.';
      }
    }, reduceMotion ? 0 : 720);
  } else if (machineStatus) {
    machineStatus.textContent = machineSelected.size ? 'Fita inserida. Jogue outras na máquina ou inicie a organização.' : 'Escolha fitas ou organize todas de uma vez.';
  }
}

function machineDelay(milliseconds, token) {
  return new Promise(resolve => window.setTimeout(() => resolve(token === machineRunToken), reduceMotion ? 0 : milliseconds));
}

async function organizeMachine() {
  if (machineBusy) {
    if (machineStatus) machineStatus.textContent = 'A máquina já está processando. Espere só um instante.';
    return;
  }
  if (!machineSelected.size) machineFileButtons.forEach(button => machineSelected.add(button.dataset.machineFile));
  updateMachineSelection();
  machineBusy = true;
  const token = ++machineRunToken;
  if (organizeMachineButton) organizeMachineButton.disabled = true;

  const stages = [
    ['intake', 'Entrada recebida. Puxando as fitas para a oficina…', 'As fitas chegaram. Segura essa pilha para mim?', 'normal', 430],
    ['analysis', 'Lendo título, temporada, episódio e marcas técnicas…', 'Separando o nome da história do barulho do arquivo.', 'focused', 620],
    ['sorting', 'Agrupando o anime e ordenando os episódios…', 'Quatro nomes diferentes. Uma mesma constelação.', 'focused', 650]
  ];
  for (const [state, status, line, mood, delay] of stages) {
    if (token !== machineRunToken) return;
    setMachineState(state);
    if (machineStatus) machineStatus.textContent = status;
    machineMood(mood, line, false);
    if (!(await machineDelay(delay, token))) return;
  }

  setMachineState('organized');
  const selectedIds = [...machineSelected].sort();
  $$('#machineEpisodes > li').forEach((item, index) => { item.hidden = !machineSelected.has(String(index + 1)); });
  const countLabel = `${selectedIds.length} ${selectedIds.length === 1 ? 'episódio' : 'episódios'}`;
  if (machineSeriesMeta) machineSeriesMeta.textContent = `Temporada 01 · ${countLabel}`;
  const finaleTitle = $('#machineFinaleTitle');
  const finaleNote = $('#machineFinaleNote');
  if (finaleTitle) finaleTitle.textContent = `${selectedIds.length} ${selectedIds.length === 1 ? 'nome organizado' : 'nomes diferentes'}. Uma temporada em ordem.`;
  if (finaleNote) finaleNote.textContent = `Padrões reconhecidos nesta tentativa: ${selectedIds.map(id => ({ 1: 'número após hífen', 2: 'S01E02', 3: 'EP03', 4: '01x04' }[id])).join(', ')}.`;
  machineOutput?.classList.add('ready');
  if (machineSeriesToggle) machineSeriesToggle.disabled = false;
  if (machineStatus) machineStatus.textContent = `Pronto: Hoshi no Tabi · temporada 01 · ${selectedIds.map(id => `E0${id}`).join(', ')}.`;
  machineMood('happy', `Tudo em ordem! ${selectedIds.length === 1 ? 'Esse episódio já sabe' : 'Agora cada episódio sabe'} exatamente onde morar.`, true);
  machineBusy = false;
  if (organizeMachineButton) organizeMachineButton.disabled = false;
}

function resetMachine() {
  machineRunToken += 1;
  machineBusy = false;
  machineSelected.clear();
  machineOutput?.classList.remove('ready');
  if (machineSeriesToggle) {
    machineSeriesToggle.disabled = true;
    machineSeriesToggle.setAttribute('aria-expanded', 'false');
    const label = machineSeriesToggle.querySelector(':scope > b');
    if (label) label.textContent = 'abrir';
  }
  if (machineEpisodes) machineEpisodes.hidden = true;
  $$('#machineEpisodes > li').forEach(item => { item.hidden = false; });
  if (machineSeriesMeta) machineSeriesMeta.textContent = 'Temporada 01 · 4 episódios';
  if (organizeMachineButton) organizeMachineButton.disabled = false;
  if (machineStatus) machineStatus.textContent = 'Escolha fitas ou organize todas de uma vez.';
  machineMood('normal', 'Pegue uma fita e jogue na máquina. Eu cuido do resto.', false);
  machine3D?.reset();
  updateMachineSelection();
}

machineFileButtons.forEach(button => {
  button.addEventListener('click', () => selectMachineFile(button.dataset.machineFile));
  button.addEventListener('dragstart', event => {
    event.dataTransfer?.setData('text/plain', button.dataset.machineFile);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy';
  });
});

machineCameraZone?.addEventListener('dragover', event => event.preventDefault());
machineCameraZone?.addEventListener('dragenter', () => machineRoot?.classList.add('drop-ready'));
machineCameraZone?.addEventListener('dragleave', event => {
  if (!machineStage?.contains(event.relatedTarget)) machineRoot?.classList.remove('drop-ready');
});
machineCameraZone?.addEventListener('drop', event => {
  event.preventDefault();
  machineRoot?.classList.remove('drop-ready');
  const id = event.dataTransfer?.getData('text/plain');
  if (id && !machineSelected.has(id)) selectMachineFile(id);
  if (id && machineStatus) machineStatus.textContent = 'A fita foi guardada dentro da máquina. Pode inserir outra ou começar.';
});
machineSeriesToggle?.addEventListener('click', () => {
  if (!machineEpisodes || machineSeriesToggle.disabled) return;
  const expanded = machineSeriesToggle.getAttribute('aria-expanded') !== 'true';
  machineSeriesToggle.setAttribute('aria-expanded', String(expanded));
  machineEpisodes.hidden = !expanded;
  const label = machineSeriesToggle.querySelector(':scope > b');
  if (label) label.textContent = expanded ? 'fechar' : 'abrir';
});
organizeMachineButton?.addEventListener('click', organizeMachine);
$('#resetMachine')?.addEventListener('click', resetMachine);

async function createMachine3D() {
  if (!machineRoot || !machineStage || !machineCanvas || !machineCameraZone) return null;
  const THREE = await import('./vendor/three.module.js');
  const renderer = new THREE.WebGLRenderer({ canvas: machineCanvas, alpha: true, antialias: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.innerWidth < 760 ? 1.1 : 1.5, window.devicePixelRatio || 1));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.12;
  machineCanvas.hidden = false;

  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0x020817, .035);
  const camera = new THREE.PerspectiveCamera(42, 1, .1, 80);
  const target = new THREE.Vector3(0, .5, 0);
  let yaw = .03;
  let pitch = .05;
  let radius = 12.2;
  let stageName = 'idle';
  let selectedIds = new Set();
  let active = true;
  let raf = 0;
  let lastFrame = 0;
  let dirty = true;
  let draggedCard = null;

  scene.add(new THREE.HemisphereLight(0x9edcff, 0x071127, 1.45));
  const cyanLight = new THREE.PointLight(0x39cfff, 75, 22, 2);
  cyanLight.position.set(-3, 5, 5);
  scene.add(cyanLight);
  const purpleLight = new THREE.PointLight(0x765cff, 58, 18, 2);
  purpleLight.position.set(5, 3, -2);
  scene.add(purpleLight);

  const floor = new THREE.GridHelper(28, 28, 0x1b92d9, 0x0b3154);
  floor.position.y = -2.25;
  floor.material.transparent = true;
  floor.material.opacity = .23;
  scene.add(floor);

  const metal = new THREE.MeshStandardMaterial({ color: 0x0b2848, metalness: .72, roughness: .29 });
  const darkMetal = new THREE.MeshStandardMaterial({ color: 0x06162e, metalness: .84, roughness: .25 });
  const glow = new THREE.MeshStandardMaterial({ color: 0x18b9ff, emissive: 0x007cc9, emissiveIntensity: 2.3, metalness: .35, roughness: .2 });
  const machine = new THREE.Group();
  scene.add(machine);
  const base = new THREE.Mesh(new THREE.BoxGeometry(8.5, .6, 4.4), darkMetal);
  base.position.y = -1.9;
  machine.add(base);
  const body = new THREE.Mesh(new THREE.BoxGeometry(4.8, 3.4, 3.3), metal);
  body.position.y = -.05;
  machine.add(body);
  const chamber = new THREE.Mesh(new THREE.CylinderGeometry(1.25, 1.25, 3.75, 32, 1, true), new THREE.MeshStandardMaterial({ color: 0x35c8ff, emissive: 0x007fc1, emissiveIntensity: 1.4, transparent: true, opacity: .22, side: THREE.DoubleSide }));
  chamber.rotation.z = Math.PI / 2;
  chamber.position.set(0, .2, 1.78);
  machine.add(chamber);
  const core = new THREE.Mesh(new THREE.IcosahedronGeometry(.78, 1), glow);
  core.position.set(0, .2, 1.85);
  machine.add(core);

  function createGear(radiusValue, teeth, color) {
    const group = new THREE.Group();
    const material = new THREE.MeshStandardMaterial({ color, metalness: .82, roughness: .25 });
    const ring = new THREE.Mesh(new THREE.TorusGeometry(radiusValue, .16, 10, 38), material);
    group.add(ring);
    for (let index = 0; index < teeth; index += 1) {
      const tooth = new THREE.Mesh(new THREE.BoxGeometry(.34, .22, .28), material);
      const angle = (index / teeth) * Math.PI * 2;
      tooth.position.set(Math.cos(angle) * radiusValue, Math.sin(angle) * radiusValue, 0);
      tooth.rotation.z = angle;
      group.add(tooth);
    }
    return group;
  }
  const gearLeft = createGear(1.15, 12, 0x2c7eb8);
  gearLeft.position.set(-2.65, .35, 1.8);
  machine.add(gearLeft);
  const gearRight = createGear(.86, 10, 0x6954cb);
  gearRight.position.set(2.55, -.25, 1.82);
  machine.add(gearRight);

  const intake = new THREE.Mesh(new THREE.BoxGeometry(2.3, .35, 1.9), metal);
  intake.position.set(-4.6, -1.25, .4);
  intake.rotation.z = -.18;
  machine.add(intake);
  const shelf = new THREE.Group();
  shelf.position.set(4.2, -.45, .5);
  machine.add(shelf);
  for (let row = 0; row < 3; row += 1) {
    const rail = new THREE.Mesh(new THREE.BoxGeometry(2.35, .12, 1.45), darkMetal);
    rail.position.y = row * .92 - .85;
    shelf.add(rail);
  }

  function labelTexture(text) {
    const label = document.createElement('canvas');
    label.width = 256;
    label.height = 128;
    const context = label.getContext('2d');
    context.fillStyle = '#06162e';
    context.fillRect(0, 0, label.width, label.height);
    context.fillStyle = '#43d8ff';
    context.font = '900 58px Inter, sans-serif';
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillText(text, 128, 64);
    const texture = new THREE.CanvasTexture(label);
    texture.colorSpace = THREE.SRGBColorSpace;
    return texture;
  }

  const cardMeshes = [];
  const cardColors = [0x1d8ac6, 0x4c64d9, 0x18a9a1, 0xc34788];
  machineFileButtons.forEach((button, index) => {
    const material = new THREE.MeshStandardMaterial({ color: cardColors[index], emissive: 0x001c35, emissiveIntensity: .35, metalness: .22, roughness: .34 });
    const card = new THREE.Mesh(new THREE.BoxGeometry(1.75, 1.1, .12), material);
    card.position.set(-5.15 + (index % 2) * 1.95, 1.65 - Math.floor(index / 2) * 1.4, 1.35);
    card.rotation.z = (index % 2 ? .08 : -.08);
    card.userData.fileId = button.dataset.machineFile;
    card.userData.home = card.position.clone();
    const face = new THREE.Mesh(new THREE.PlaneGeometry(1.25, .6), new THREE.MeshBasicMaterial({ map: labelTexture(`0${index + 1}`), transparent: true }));
    face.position.z = .071;
    card.add(face);
    scene.add(card);
    cardMeshes.push(card);
  });

  const episodeBlocks = [];
  for (let index = 0; index < 4; index += 1) {
    const block = new THREE.Mesh(new THREE.BoxGeometry(1.75, .62, .72), new THREE.MeshStandardMaterial({ color: 0x1599c7, emissive: 0x005382, emissiveIntensity: 1.1, metalness: .32, roughness: .35 }));
    block.position.set(4.2, -.8 + index * .58, .65);
    block.scale.setScalar(.001);
    const face = new THREE.Mesh(new THREE.PlaneGeometry(.92, .34), new THREE.MeshBasicMaterial({ map: labelTexture(`E0${index + 1}`), transparent: true }));
    face.position.z = .371;
    block.add(face);
    scene.add(block);
    episodeBlocks.push(block);
  }

  const textureLoader = new THREE.TextureLoader();
  const moodTextures = new Map();
  const bakiMaterial = new THREE.SpriteMaterial({ transparent: true, depthWrite: false });
  const bakiSprite = new THREE.Sprite(bakiMaterial);
  bakiSprite.position.set(4.7, 2.15, 1.6);
  bakiSprite.scale.set(3.1, 3.1, 1);
  scene.add(bakiSprite);
  function setMood(source) {
    if (moodTextures.has(source)) {
      bakiMaterial.map = moodTextures.get(source);
      bakiMaterial.needsUpdate = true;
      dirty = true;
      return;
    }
    textureLoader.load(source, texture => {
      texture.colorSpace = THREE.SRGBColorSpace;
      moodTextures.set(source, texture);
      bakiMaterial.map = texture;
      bakiMaterial.needsUpdate = true;
      dirty = true;
    });
  }
  setMood('./assets/baki-normal.png');

  function updateCamera() {
    const x = Math.sin(yaw) * Math.cos(pitch) * radius;
    const y = Math.sin(pitch) * radius + 1;
    const z = Math.cos(yaw) * Math.cos(pitch) * radius;
    camera.position.set(x, y, z);
    camera.lookAt(target);
    dirty = true;
  }
  updateCamera();

  function resize() {
    const rect = machineStage.getBoundingClientRect();
    const width = Math.max(1, Math.floor(rect.width));
    const height = Math.max(1, Math.floor(rect.height));
    renderer.setSize(width, height, false);
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    dirty = true;
  }
  new ResizeObserver(resize).observe(machineStage);
  resize();

  function render(time = 0) {
    raf = 0;
    if (!active || document.hidden) return;
    const elapsed = Math.min(.05, (time - lastFrame) / 1000 || 0);
    const processing = ['intake', 'analysis', 'sorting'].includes(stageName);
    if (!reduceMotion && time - lastFrame >= 22) {
      lastFrame = time;
      gearLeft.rotation.z += elapsed * (processing ? 2.5 : .22);
      gearRight.rotation.z -= elapsed * (processing ? 3.1 : .28);
      core.rotation.y += elapsed * (processing ? 2.3 : .38);
      core.rotation.x += elapsed * .25;
      const pulse = 1 + Math.sin(time * .003) * (processing ? .12 : .035);
      core.scale.setScalar(pulse);
      cardMeshes.forEach((card, index) => {
        const selected = machineSelected.has(card.userData.fileId);
        let destination = card.userData.home;
        let scaleTarget = selected ? 1.06 : 1;
        if (stageName === 'selected' && selected) {
          const insertionProgress = reduceMotion ? 1 : Math.min(1, Math.max(0, (time - (card.userData.insertedAt || time)) / 780));
          destination = insertionProgress < .58
            ? new THREE.Vector3(-2.75 + (index % 2) * .18, -.5 + index * .08, 1.58)
            : new THREE.Vector3(-.25, .05, 1.72);
          scaleTarget = insertionProgress < .58 ? 1.06 : Math.max(.001, 1.06 * (1 - (insertionProgress - .58) / .42));
        }
        if (['intake', 'analysis', 'sorting', 'organized'].includes(stageName) && selected) {
          destination = new THREE.Vector3(-.25, .05, 1.72);
          scaleTarget = .001;
        }
        if (card !== draggedCard) {
          if (reduceMotion) card.position.copy(destination); else card.position.lerp(destination, .085);
        }
        card.material.emissiveIntensity = selected ? 1.35 : .35;
        const scaleVector = new THREE.Vector3(scaleTarget, scaleTarget, Math.max(.001, scaleTarget));
        if (reduceMotion) card.scale.copy(scaleVector); else card.scale.lerp(scaleVector, .11);
      });
      episodeBlocks.forEach((block, index) => {
        const value = stageName === 'organized' && selectedIds.has(String(index + 1)) ? 1 : .001;
        block.scale.lerp(new THREE.Vector3(value, value, value), .09 + index * .008);
      });
      dirty = true;
    }
    if (dirty) {
      renderer.render(scene, camera);
      dirty = false;
    }
    if (!reduceMotion || processing) raf = requestAnimationFrame(render);
  }

  function setActive(value) {
    active = value;
    if (active && !raf) raf = requestAnimationFrame(render);
    if (!active && raf) { cancelAnimationFrame(raf); raf = 0; }
  }
  function setStage(value) {
    stageName = value;
    dirty = true;
    if (active && !raf) raf = requestAnimationFrame(render);
  }
  function setSelected(ids) {
    const nextSelectedIds = new Set(ids);
    const now = performance.now();
    cardMeshes.forEach(card => {
      const entering = nextSelectedIds.has(card.userData.fileId) && !selectedIds.has(card.userData.fileId);
      if (entering) card.userData.insertedAt = now;
      if (!nextSelectedIds.has(card.userData.fileId)) card.userData.insertedAt = null;
    });
    selectedIds = nextSelectedIds;
    cardMeshes.forEach(card => { card.material.emissiveIntensity = ids.includes(card.userData.fileId) ? 1.35 : .35; });
    dirty = true;
    if (active && !raf) raf = requestAnimationFrame(render);
  }
  function reset() {
    stageName = 'idle';
    cardMeshes.forEach(card => {
      card.position.copy(card.userData.home);
      card.scale.set(1, 1, 1);
      card.material.emissiveIntensity = .35;
      card.userData.insertedAt = null;
    });
    episodeBlocks.forEach(block => block.scale.setScalar(.001));
    yaw = .03;
    pitch = .05;
    updateCamera();
    if (active && !raf) raf = requestAnimationFrame(render);
  }

  const raycaster = new THREE.Raycaster();
  const pointer = new THREE.Vector2();
  const dragPlane = new THREE.Plane(new THREE.Vector3(0, 0, 1), -1.55);
  const dragPoint = new THREE.Vector3();
  let dragStart = null;
  let lastPointer = null;
  function pointRayAt(event) {
    const bounds = machineCanvas.getBoundingClientRect();
    pointer.x = ((event.clientX - bounds.left) / bounds.width) * 2 - 1;
    pointer.y = -((event.clientY - bounds.top) / bounds.height) * 2 + 1;
    raycaster.setFromCamera(pointer, camera);
  }
  machineCameraZone.addEventListener('pointerdown', event => {
    dragStart = { x: event.clientX, y: event.clientY };
    lastPointer = { x: event.clientX, y: event.clientY };
    pointRayAt(event);
    draggedCard = raycaster.intersectObjects(cardMeshes, false)[0]?.object || null;
    machineCameraZone.classList.add(draggedCard ? 'dragging-card' : 'dragging');
    if (draggedCard) {
      draggedCard.material.emissiveIntensity = 2.2;
      machineRoot?.classList.add('drop-ready');
      if (machineStatus) machineStatus.textContent = 'Leve a fita até o centro da máquina e solte.';
    }
    machineCameraZone.setPointerCapture?.(event.pointerId);
  });
  machineCameraZone.addEventListener('pointermove', event => {
    if (!lastPointer) return;
    if (draggedCard) {
      pointRayAt(event);
      if (raycaster.ray.intersectPlane(dragPlane, dragPoint)) {
        draggedCard.position.set(Math.max(-5.6, Math.min(3.2, dragPoint.x)), Math.max(-1.45, Math.min(2.35, dragPoint.y)), 1.55);
        draggedCard.rotation.z = Math.max(-.22, Math.min(.22, (event.clientX - dragStart.x) * .002));
        dirty = true;
        if (active && !raf) raf = requestAnimationFrame(render);
      }
      lastPointer = { x: event.clientX, y: event.clientY };
      return;
    }
    yaw -= (event.clientX - lastPointer.x) * .005;
    pitch = Math.max(-.16, Math.min(.32, pitch + (event.clientY - lastPointer.y) * .003));
    lastPointer = { x: event.clientX, y: event.clientY };
    updateCamera();
    if (active && !raf) raf = requestAnimationFrame(render);
  });
  machineCameraZone.addEventListener('pointerup', event => {
    machineCameraZone.classList.remove('dragging', 'dragging-card');
    machineRoot?.classList.remove('drop-ready');
    const moved = dragStart && Math.hypot(event.clientX - dragStart.x, event.clientY - dragStart.y) > 6;
    lastPointer = null;
    if (draggedCard) {
      const id = draggedCard.userData.fileId;
      const enteredMachine = moved && draggedCard.position.x > -3.65 && draggedCard.position.x < 2.5 && draggedCard.position.y > -1.55 && draggedCard.position.y < 2.15;
      draggedCard.rotation.z = Number(id) % 2 ? .08 : -.08;
      draggedCard.material.emissiveIntensity = machineSelected.has(id) ? 1.35 : .35;
      draggedCard = null;
      if (enteredMachine) {
        if (!machineSelected.has(id)) selectMachineFile(id, id === '4');
        if (machineStatus) machineStatus.textContent = 'A fita foi guardada dentro da máquina. Pode inserir outra ou começar.';
        machineMood('normal', 'Peguei! Ela está segura aqui dentro.', false);
      } else if (!moved) {
        selectMachineFile(id);
      } else if (machineStatus) {
        machineStatus.textContent = 'Quase! Solte a fita sobre o centro iluminado da máquina.';
      }
      dragStart = null;
      dirty = true;
      if (active && !raf) raf = requestAnimationFrame(render);
      return;
    }
    if (moved || !dragStart) { dragStart = null; return; }
    dragStart = null;
  });
  machineCameraZone.addEventListener('pointercancel', () => {
    draggedCard = null;
    dragStart = null;
    lastPointer = null;
    machineRoot?.classList.remove('drop-ready');
    machineCameraZone.classList.remove('dragging', 'dragging-card');
  });
  machineCameraZone.addEventListener('keydown', event => {
    const keys = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home'];
    if (!keys.includes(event.key)) return;
    event.preventDefault();
    if (event.key === 'Home') { yaw = .03; pitch = .05; }
    if (event.key === 'ArrowLeft') yaw -= .1;
    if (event.key === 'ArrowRight') yaw += .1;
    if (event.key === 'ArrowUp') pitch = Math.min(.32, pitch + .07);
    if (event.key === 'ArrowDown') pitch = Math.max(-.16, pitch - .07);
    updateCamera();
    if (active && !raf) raf = requestAnimationFrame(render);
  });

  setActive(true);
  return { setActive, setStage, setSelected, setMood, reset };
}

async function loadMachine3D() {
  if (machineLoadStarted || !machineRoot) return;
  machineLoadStarted = true;
  try {
    machine3D = await createMachine3D();
    if (!machine3D) throw new Error('Cena indisponível');
    machineRoot.classList.add('webgl-ready');
    machine3D.setSelected([...machineSelected]);
    machine3D.setStage(machineRoot.dataset.state || 'idle');
  } catch (error) {
    console.info('AniT: demonstração 3D indisponível; usando modo compatível.', error);
    machineRoot.classList.add('webgl-fallback');
    if (machineCompat) machineCompat.hidden = false;
    if (machinePoster) machinePoster.querySelector('p').innerHTML = '<span></span> Modo visual compatível ativo';
  }
}

if (machineRoot && 'IntersectionObserver' in window) {
  const machineObserver = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (entry.isIntersecting) loadMachine3D();
      machine3D?.setActive(entry.isIntersecting && !document.hidden);
    });
  }, { rootMargin: '700px 0px', threshold: .01 });
  machineObserver.observe(machineRoot);
} else {
  loadMachine3D();
}
document.addEventListener('visibilitychange', () => machine3D?.setActive(!document.hidden && Boolean(machineRoot?.getBoundingClientRect().bottom > 0)));

const journeyStates = {
  quero: { progress: 0, time: 'Ainda não iniciado', percent: '0%', action: 'Começar o episódio', message: 'Adicionado à jornada. Quando começar, eu guardo o caminho.', mood: 'normal' },
  assistindo: { progress: 77, time: '18:42 de 24:10', percent: '77%', action: 'Continuar do ponto salvo', message: 'Seu ponto continua aqui. Nem um segundo da história se perde.', mood: 'happy' },
  concluido: { progress: 100, time: '24:10 de 24:10', percent: '100%', action: 'Assistir novamente', message: 'Concluído! Mais uma memória acesa na sua jornada.', mood: 'happy' }
};
let currentJourneyState = 'assistindo';

function setJourneyState(name, announce = true) {
  const state = journeyStates[name] || journeyStates.assistindo;
  currentJourneyState = name;
  $$('.state-switcher button').forEach(button => {
    const active = button.dataset.state === name;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  const progress = $('#journeyProgress');
  const time = $('#journeyTime');
  const percent = $('#journeyPercent');
  const action = $('#resumeJourney');
  if (progress) progress.style.width = `${state.progress}%`;
  if (time) time.textContent = state.time;
  if (percent) percent.textContent = state.percent;
  if (action) action.innerHTML = `<span aria-hidden="true">▶</span> ${state.action}`;
  const stamp = $('#memoryStamp');
  stamp?.classList.remove('flash');
  requestAnimationFrame(() => stamp?.classList.add('flash'));
  if (announce) speak(state.message, state.mood);
}

$$('.state-switcher button').forEach(button => button.addEventListener('click', () => setJourneyState(button.dataset.state)));
$('#resumeJourney')?.addEventListener('click', () => {
  if (currentJourneyState === 'concluido') return setJourneyState('assistindo');
  if (currentJourneyState === 'quero') return setJourneyState('assistindo');
  const progress = $('#journeyProgress');
  const percent = $('#journeyPercent');
  const time = $('#journeyTime');
  if (progress) progress.style.width = '84%';
  if (percent) percent.textContent = '84%';
  if (time) time.textContent = '20:19 de 24:10';
  speak('Retomado. O céu estava esperando exatamente neste ponto.', 'happy');
});

const moodAssets = {
  normal: './assets/baki-normal.png',
  happy: './assets/baki-happy.png',
  focused: './assets/baki-angry.png',
  sleepy: './assets/baki-sleepy.png'
};
const expression = $('#bakiExpression');
const bakiLine = $('#bakiLine');

$$('.mood').forEach(button => {
  button.addEventListener('click', () => {
    const mood = button.dataset.mood;
    if (!moodAssets[mood] || !expression) return;
    $$('.mood').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
    expression.classList.add('swapping');
    window.setTimeout(() => {
      expression.src = moodAssets[mood];
      expression.alt = `Baki-Pi — humor ${button.textContent.trim().toLowerCase()}`;
      expression.classList.remove('swapping');
    }, reduceMotion ? 0 : 170);
    const message = button.dataset.label || '';
    if (bakiLine) bakiLine.textContent = `“${message}”`;
    speak(message, mood, 4000);
  });
});

const oracleData = {
  aventura: { title: 'AVENTURA', phrase: 'Hoje a sua curiosidade aponta para um mundo que ainda não tem mapa.', colors: ['#04142f', '#087dbb', '#7069ff'], mood: 'happy' },
  conforto: { title: 'CONFORTO', phrase: 'Uma história familiar também pode abrir uma janela nova por dentro.', colors: ['#160d24', '#935f91', '#ffb878'], mood: 'normal' },
  misterio: { title: 'MISTÉRIO', phrase: 'Há uma pista escondida no episódio que você quase deixou para amanhã.', colors: ['#020712', '#153b6b', '#9d5cff'], mood: 'focused' }
};
let selectedOracle = 'aventura';
let lastShareText = '';

$$('.oracle-option').forEach(button => {
  button.addEventListener('click', () => {
    selectedOracle = button.dataset.oracle;
    $$('.oracle-option').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
  });
});

function loadImage(src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = reject;
    image.src = src;
  });
}

function wrapCanvasText(context, text, x, y, maxWidth, lineHeight) {
  const words = text.split(' ');
  let line = '';
  let row = 0;
  words.forEach(word => {
    const test = `${line}${word} `;
    if (context.measureText(test).width > maxWidth && line) {
      context.fillText(line.trim(), x, y + row * lineHeight);
      line = `${word} `;
      row += 1;
    } else {
      line = test;
    }
  });
  context.fillText(line.trim(), x, y + row * lineHeight);
  return row + 1;
}

function seededRandom(seed) {
  let value = seed % 2147483647;
  return () => { value = value * 16807 % 2147483647; return (value - 1) / 2147483646; };
}

async function generateJourneyCard() {
  const canvas = $('#journeyCard');
  const placeholder = $('#cardPlaceholder');
  const shareActions = $('#shareActions');
  const download = $('#downloadJourneyCard');
  const status = $('#shareStatus');
  const data = oracleData[selectedOracle];
  if (!canvas || !data) return;
  const button = $('#generateJourneyCard');
  if (button) { button.disabled = true; button.textContent = 'Acendendo…'; }
  try {
    await document.fonts?.ready;
    const [baki, logo] = await Promise.all([loadImage(moodAssets[data.mood]), loadImage('./assets/anit-logo.png')]);
    const context = canvas.getContext('2d');
    const gradient = context.createLinearGradient(0, 0, 1080, 1080);
    gradient.addColorStop(0, data.colors[0]);
    gradient.addColorStop(.55, data.colors[1]);
    gradient.addColorStop(1, data.colors[2]);
    context.fillStyle = gradient;
    context.fillRect(0, 0, 1080, 1080);

    const glow = context.createRadialGradient(730, 360, 20, 730, 360, 520);
    glow.addColorStop(0, 'rgba(99,231,255,.36)');
    glow.addColorStop(1, 'rgba(4,9,23,0)');
    context.fillStyle = glow;
    context.fillRect(0, 0, 1080, 1080);

    const random = seededRandom(selectedOracle.length * 7919);
    for (let i = 0; i < 92; i += 1) {
      const x = random() * 1080;
      const y = random() * 1080;
      const r = random() * 2.2 + .4;
      context.beginPath();
      context.arc(x, y, r, 0, Math.PI * 2);
      context.fillStyle = `rgba(210,246,255,${.18 + random() * .62})`;
      context.fill();
    }

    context.strokeStyle = 'rgba(126,221,255,.22)';
    context.lineWidth = 2;
    [220, 310, 405].forEach(radius => {
      context.beginPath();
      context.arc(775, 565, radius, Math.PI * .18, Math.PI * 1.7);
      context.stroke();
    });

    context.drawImage(logo, 68, 66, 210, 118);
    context.drawImage(baki, 565, 280, 490, 490);
    context.fillStyle = 'rgba(1,7,21,.68)';
    context.fillRect(0, 750, 1080, 330);
    context.fillStyle = '#66e6ff';
    context.font = '800 26px Inter, sans-serif';
    context.letterSpacing = '4px';
    context.fillText('SINAL DA PRÓXIMA HISTÓRIA', 68, 820);
    context.fillStyle = '#ffffff';
    context.font = '900 76px Inter, sans-serif';
    context.letterSpacing = '-2px';
    context.fillText(data.title, 68, 910);
    context.font = '600 28px Inter, sans-serif';
    context.fillStyle = 'rgba(236,248,255,.86)';
    wrapCanvasText(context, data.phrase, 68, 966, 880, 38);
    context.font = '700 20px Inter, sans-serif';
    context.fillStyle = 'rgba(174,210,232,.7)';
    context.fillText('Criado com Baki-Pi · AniT', 68, 1040);

    canvas.classList.add('ready');
    if (placeholder) placeholder.hidden = true;
    if (shareActions) shareActions.hidden = false;
    const dataUrl = canvas.toDataURL('image/png');
    if (download) download.href = dataUrl;
    lastShareText = `${data.title}: ${data.phrase} — Baki-Pi no AniT`;
    if (status) status.textContent = 'Cartão pronto. Baixe a imagem ou compartilhe a frase.';
    speak('Seu sinal está aceso. Essa memória já pode viajar com você.', data.mood, 5400);
  } catch {
    if (status) status.textContent = 'Não foi possível gerar o cartão neste navegador.';
  } finally {
    if (button) { button.disabled = false; button.textContent = 'Acender meu cartão'; }
  }
}

$('#generateJourneyCard')?.addEventListener('click', generateJourneyCard);
$('#shareJourneyCard')?.addEventListener('click', async () => {
  if (!lastShareText) return;
  const status = $('#shareStatus');
  try {
    if (navigator.share) {
      await navigator.share({ title: 'Minha próxima história · AniT', text: lastShareText, url: 'https://pedrovitor-dev.github.io/AniT/' });
      if (status) status.textContent = 'Frase compartilhada.';
    } else {
      await navigator.clipboard.writeText(`${lastShareText}\nhttps://pedrovitor-dev.github.io/AniT/`);
      if (status) status.textContent = 'Frase copiada para compartilhar.';
    }
  } catch (error) {
    if (error?.name !== 'AbortError' && status) status.textContent = 'Compartilhamento cancelado. Você ainda pode baixar o cartão.';
  }
});

const demoPlayer = $('#demoPlayer');
const demoNumber = $('#demoNumber');
const demoName = $('#demoName');
const demoDescription = $('#demoDescription');

$$('.demo-choice').forEach(button => {
  button.addEventListener('click', () => {
    const nextVideo = button.dataset.video;
    if (!demoPlayer || !nextVideo) return;
    $$('.demo-choice').forEach(choice => {
      const active = choice === button;
      choice.classList.toggle('active', active);
      choice.setAttribute('aria-pressed', String(active));
    });
    if (demoNumber) demoNumber.textContent = button.dataset.number || '';
    if (demoName) demoName.textContent = button.dataset.title || '';
    if (demoDescription) demoDescription.textContent = button.dataset.description || '';
    demoPlayer.setAttribute('aria-label', `Demonstração: ${button.dataset.title || 'AniT'}`);
    demoPlayer.pause();
    demoPlayer.src = nextVideo;
    demoPlayer.load();
    demoPlayer.play().catch(() => {});
    speak(`Agora: ${button.dataset.title}. É o aplicativo real em movimento.`, 'focused', 3300);
  });
});

if (demoPlayer && 'IntersectionObserver' in window) {
  const videoObserver = new IntersectionObserver(entries => {
    if (!entries[0].isIntersecting) demoPlayer.pause();
  }, { threshold: .18 });
  videoObserver.observe(demoPlayer);
}

function starPalette() {
  const atmosphere = document.documentElement.dataset.atmosphere;
  if (atmosphere === 'nostalgia') return ['255,211,177', '222,151,220'];
  if (atmosphere === 'maratona') return ['105,249,255', '98,153,255'];
  return ['157,222,255', '151,146,255'];
}

function resizeStars() {
  if (!canvas || !starContext) return;
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.floor(window.innerWidth * dpr);
  canvas.height = Math.floor(window.innerHeight * dpr);
  canvas.style.width = `${window.innerWidth}px`;
  canvas.style.height = `${window.innerHeight}px`;
  starContext.setTransform(dpr, 0, 0, dpr, 0, 0);
  const count = Math.min(120, Math.floor(window.innerWidth * window.innerHeight / 13500));
  stars = Array.from({ length: count }, (_, index) => ({
    x: Math.random() * window.innerWidth,
    y: Math.random() * window.innerHeight,
    radius: Math.random() * 1.35 + .3,
    alpha: Math.random() * .58 + .18,
    speed: Math.random() * .006 + .002,
    phase: Math.random() * Math.PI * 2,
    color: index % 5 === 0 ? 1 : 0
  }));
}

function recolorStars() { if (starContext) drawStars(performance.now(), true); }

function drawStars(time = 0, once = false) {
  if (!canvas || !starContext || document.hidden) return;
  const colors = starPalette();
  starContext.clearRect(0, 0, window.innerWidth, window.innerHeight);
  stars.forEach(star => {
    const pulse = reduceMotion ? 1 : .7 + Math.sin(time * star.speed + star.phase) * .3;
    starContext.beginPath();
    starContext.arc(star.x, star.y, star.radius, 0, Math.PI * 2);
    starContext.fillStyle = `rgba(${colors[star.color]},${Math.max(.07, star.alpha * pulse)})`;
    starContext.fill();
  });
  if (!once && !reduceMotion) starAnimation = requestAnimationFrame(drawStars);
}

resizeStars();
drawStars();
window.addEventListener('resize', resizeStars, { passive: true });

document.addEventListener('visibilitychange', () => {
  document.body.dataset.pageHidden = String(document.hidden);
  if (document.hidden) {
    cancelAnimationFrame(starAnimation);
    demoPlayer?.pause();
  } else if (!reduceMotion) {
    cancelAnimationFrame(starAnimation);
    starAnimation = requestAnimationFrame(drawStars);
  }
});
