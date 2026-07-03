// ══════════════════════════════════════════════════════════════
// Lips Song Creator - Piano Roll Canvas Renderer
// ══════════════════════════════════════════════════════════════

const NOTE_HEIGHT = 16;
const MIN_NOTE = 36;  // C2
const MAX_NOTE = 84;  // C7
const NOTE_RANGE = MAX_NOTE - MIN_NOTE;
const NOTE_NAMES = ['C','C#','D','D#','E','F','F#','G','G#','A','A#','B'];

let state = {
    canvas: null,
    ctx: null,
    notes: [],
    lyrics: [],
    pixelsPerSecond: 80,
    scrollX: 0,
    scrollY: 0,
    playhead: 0,
    selectedIdx: -1,
    hoveredIdx: -1,
    totalDuration: 120,
    isDragging: false,
    dragStartX: 0,
    dragStartScrollX: 0,
    dotNetRef: null,
    animFrame: null,
};

// ── Public API (called from Blazor) ─────────────────────────

window.pianoRoll = {

    init(canvasId, dotNetRef) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        state.canvas = canvas;
        state.ctx = canvas.getContext('2d');
        state.dotNetRef = dotNetRef;

        resizeCanvas();
        window.addEventListener('resize', resizeCanvas);

        canvas.addEventListener('wheel', onWheel, { passive: false });
        canvas.addEventListener('mousedown', onMouseDown);
        canvas.addEventListener('mousemove', onMouseMove);
        canvas.addEventListener('mouseup', onMouseUp);
        canvas.addEventListener('mouseleave', () => { state.isDragging = false; });

        render();
    },

    setNotes(notes) {
        state.notes = notes || [];
        if (notes.length > 0) {
            const maxTime = Math.max(...notes.map(n => n.time + n.length));
            state.totalDuration = Math.max(maxTime + 5, 30);
        }
        render();
    },

    setLyrics(lyrics) {
        state.lyrics = lyrics || [];
    },

    setPlayhead(time) {
        state.playhead = time;
        render();
    },

    scrollTo(time) {
        state.scrollX = time * state.pixelsPerSecond - state.canvas.width / 3;
        if (state.scrollX < 0) state.scrollX = 0;
        render();
    },

    setZoom(pps) {
        state.pixelsPerSecond = Math.max(20, Math.min(400, pps));
        render();
    },

    getState() {
        return {
            selectedIdx: state.selectedIdx,
            scrollX: state.scrollX,
            zoom: state.pixelsPerSecond,
            playhead: state.playhead,
        };
    },

    destroy() {
        window.removeEventListener('resize', resizeCanvas);
        if (state.animFrame) cancelAnimationFrame(state.animFrame);
        state.canvas = null;
        state.ctx = null;
    }
};

// ── Canvas resize ───────────────────────────────────────────

function resizeCanvas() {
    if (!state.canvas) return;
    const parent = state.canvas.parentElement;
    state.canvas.width = parent.clientWidth;
    state.canvas.height = parent.clientHeight;
    render();
}

// ── Coordinate helpers ──────────────────────────────────────

function timeToX(t) {
    return t * state.pixelsPerSecond - state.scrollX;
}

function xToTime(x) {
    return (x + state.scrollX) / state.pixelsPerSecond;
}

function noteToY(midiNote) {
    // Higher notes at top
    const row = MAX_NOTE - midiNote;
    return row * NOTE_HEIGHT - state.scrollY;
}

function yToNote(y) {
    const row = (y + state.scrollY) / NOTE_HEIGHT;
    return MAX_NOTE - Math.floor(row);
}

function isBlackKey(midiNote) {
    const n = midiNote % 12;
    return [1, 3, 6, 8, 10].includes(n);
}

// ── Rendering ───────────────────────────────────────────────

function render() {
    if (!state.ctx || !state.canvas) return;
    const ctx = state.ctx;
    const W = state.canvas.width;
    const H = state.canvas.height;

    ctx.clearRect(0, 0, W, H);

    drawGrid(ctx, W, H);
    drawNotes(ctx, W, H);
    drawPlayhead(ctx, W, H);
}

function drawGrid(ctx, W, H) {
    // Horizontal lines (pitch rows)
    for (let note = MIN_NOTE; note <= MAX_NOTE; note++) {
        const y = noteToY(note);
        if (y < -NOTE_HEIGHT || y > H) continue;

        // Row background
        ctx.fillStyle = isBlackKey(note) ? '#0d0d18' : '#111122';
        ctx.fillRect(0, y, W, NOTE_HEIGHT);

        // Row border
        const isCNote = note % 12 === 0;
        ctx.strokeStyle = isCNote ? '#2a2a50' : '#181830';
        ctx.lineWidth = isCNote ? 1 : 0.5;
        ctx.beginPath();
        ctx.moveTo(0, y + NOTE_HEIGHT);
        ctx.lineTo(W, y + NOTE_HEIGHT);
        ctx.stroke();
    }

    // Vertical lines (time)
    const startTime = Math.max(0, xToTime(0));
    const endTime = xToTime(W);

    // Determine grid interval based on zoom
    let interval = 1; // seconds
    if (state.pixelsPerSecond < 30) interval = 5;
    else if (state.pixelsPerSecond < 60) interval = 2;
    else if (state.pixelsPerSecond > 200) interval = 0.5;
    else if (state.pixelsPerSecond > 300) interval = 0.25;

    const gridStart = Math.floor(startTime / interval) * interval;
    for (let t = gridStart; t <= endTime; t += interval) {
        const x = timeToX(t);
        const isMajor = Math.abs(t % 5) < 0.001;

        ctx.strokeStyle = isMajor ? '#2a2a55' : '#1a1a35';
        ctx.lineWidth = isMajor ? 1 : 0.5;
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, H);
        ctx.stroke();

        // Time label
        if (isMajor || interval >= 1) {
            const min = Math.floor(t / 60);
            const sec = Math.floor(t % 60);
            ctx.fillStyle = '#444466';
            ctx.font = '9px sans-serif';
            ctx.fillText(`${min}:${sec.toString().padStart(2, '0')}`, x + 3, 11);
        }
    }
}

function drawNotes(ctx, W, H) {
    for (let i = 0; i < state.notes.length; i++) {
        const n = state.notes[i];
        const x = timeToX(n.time);
        const w = n.length * state.pixelsPerSecond;
        const midiNote = (n.octave || 0) * 12 + (n.fIdx || 0);
        const y = noteToY(midiNote);

        // Culling
        if (x + w < 0 || x > W || y + NOTE_HEIGHT < 0 || y > H) continue;

        const isSelected = i === state.selectedIdx;
        const isHovered = i === state.hoveredIdx;

        // Note rectangle
        let color = '#00aadd';
        if (n.type === 'golden') color = '#ddaa00';
        else if (n.type === 'freestyle') color = '#00cc66';

        const alpha = isSelected ? 1.0 : isHovered ? 0.85 : 0.7;
        ctx.globalAlpha = alpha;

        // Glow effect
        if (isSelected || isHovered) {
            ctx.shadowColor = color;
            ctx.shadowBlur = isSelected ? 12 : 6;
        }

        // Fill
        ctx.fillStyle = color;
        const r = 3;
        roundRect(ctx, x, y + 1, Math.max(w, 4), NOTE_HEIGHT - 2, r);
        ctx.fill();

        ctx.shadowBlur = 0;
        ctx.globalAlpha = 1;

        // Border
        if (isSelected) {
            ctx.strokeStyle = '#ffffff';
            ctx.lineWidth = 1.5;
            roundRect(ctx, x, y + 1, Math.max(w, 4), NOTE_HEIGHT - 2, r);
            ctx.stroke();
        }

        // Syllable text inside note
        if (n.syllable && w > 12) {
            ctx.fillStyle = isSelected ? '#fff' : 'rgba(255,255,255,0.8)';
            ctx.font = '10px sans-serif';
            ctx.fillText(n.syllable, x + 3, y + NOTE_HEIGHT - 4, w - 6);
        }
    }
}

function drawPlayhead(ctx, W, H) {
    const x = timeToX(state.playhead);
    if (x < 0 || x > W) return;

    ctx.strokeStyle = '#ff00aa';
    ctx.lineWidth = 2;
    ctx.shadowColor = '#ff00aa';
    ctx.shadowBlur = 8;
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, H);
    ctx.stroke();
    ctx.shadowBlur = 0;

    // Playhead triangle at top
    ctx.fillStyle = '#ff00aa';
    ctx.beginPath();
    ctx.moveTo(x - 5, 0);
    ctx.lineTo(x + 5, 0);
    ctx.lineTo(x, 8);
    ctx.closePath();
    ctx.fill();
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x + r, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
}

// ── Mouse Interaction ───────────────────────────────────────

function onWheel(e) {
    e.preventDefault();

    if (e.ctrlKey) {
        // Zoom
        const factor = e.deltaY > 0 ? 0.85 : 1.18;
        const mouseTime = xToTime(e.offsetX);
        state.pixelsPerSecond *= factor;
        state.pixelsPerSecond = Math.max(20, Math.min(400, state.pixelsPerSecond));
        // Keep mouse position stable
        state.scrollX = mouseTime * state.pixelsPerSecond - e.offsetX;
        if (state.scrollX < 0) state.scrollX = 0;
    } else if (e.shiftKey) {
        // Vertical scroll
        state.scrollY += e.deltaY * 0.5;
        state.scrollY = Math.max(0, Math.min(NOTE_RANGE * NOTE_HEIGHT - state.canvas.height + 50, state.scrollY));
    } else {
        // Horizontal scroll
        state.scrollX += e.deltaY * 1.5;
        if (state.scrollX < 0) state.scrollX = 0;
    }

    render();
}

function onMouseDown(e) {
    if (e.button === 1 || (e.button === 0 && e.altKey)) {
        // Middle click or Alt+click: start dragging
        state.isDragging = true;
        state.dragStartX = e.clientX;
        state.dragStartScrollX = state.scrollX;
        e.preventDefault();
        return;
    }

    // Left click: try to select a note
    const clickTime = xToTime(e.offsetX);
    const clickNote = yToNote(e.offsetY);

    let found = -1;
    for (let i = 0; i < state.notes.length; i++) {
        const n = state.notes[i];
        const midiNote = (n.octave || 0) * 12 + (n.fIdx || 0);
        if (midiNote === clickNote &&
            clickTime >= n.time && clickTime <= n.time + n.length) {
            found = i;
            break;
        }
    }

    if (found !== state.selectedIdx) {
        state.selectedIdx = found;
        render();

        if (state.dotNetRef) {
            state.dotNetRef.invokeMethodAsync('OnNoteSelected', found);
        }
    }
}

function onMouseMove(e) {
    if (state.isDragging) {
        const dx = e.clientX - state.dragStartX;
        state.scrollX = state.dragStartScrollX - dx;
        if (state.scrollX < 0) state.scrollX = 0;
        render();
        return;
    }

    // Hover detection
    const hoverTime = xToTime(e.offsetX);
    const hoverNote = yToNote(e.offsetY);
    let found = -1;

    for (let i = 0; i < state.notes.length; i++) {
        const n = state.notes[i];
        const midiNote = (n.octave || 0) * 12 + (n.fIdx || 0);
        if (midiNote === hoverNote &&
            hoverTime >= n.time && hoverTime <= n.time + n.length) {
            found = i;
            break;
        }
    }

    if (found !== state.hoveredIdx) {
        state.hoveredIdx = found;
        state.canvas.style.cursor = found >= 0 ? 'pointer' : 'default';
        render();
    }
}

function onMouseUp(e) {
    state.isDragging = false;
}
