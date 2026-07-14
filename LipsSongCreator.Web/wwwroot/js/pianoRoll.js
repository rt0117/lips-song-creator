// ══════════════════════════════════════════════════════════════
// Lips Song Creator - Piano Roll Canvas Renderer + Editor
//   - Noten anklicken, verschieben (Zeit + Tonhoehe), Kanten ziehen
//   - Audio-Wiedergabe mit Playhead-Sync (HTML5 Audio)
//   - Preview-Bereich (Timeframe) markieren und verschieben
// ══════════════════════════════════════════════════════════════

const NOTE_HEIGHT = 16;
const MIN_NOTE = 36;  // C2
const MAX_NOTE = 84;  // C7
const NOTE_RANGE = MAX_NOTE - MIN_NOTE;
const EDGE_PX = 6;    // Griffbreite fuer Resize an Notenkanten
const PREVIEW_EDGE_PX = 8;

let state = {
    canvas: null,
    ctx: null,
    notes: [],
    pixelsPerSecond: 80,
    scrollX: 0,
    scrollY: 0,
    playhead: 0,
    selectedIdx: -1,
    hoveredIdx: -1,
    totalDuration: 120,
    dotNetRef: null,

    // Pan
    isPanning: false,
    panStartX: 0,
    panStartScrollX: 0,

    // Note-Editing
    editMode: true,
    dragMode: null,       // 'move' | 'resize-l' | 'resize-r' | 'preview-move' | 'preview-l' | 'preview-r' | null
    dragNoteIdx: -1,
    dragStartTime: 0,
    dragStartLen: 0,
    dragStartNote: 0,
    dragMouseTime: 0,
    dragChanged: false,

    // Audio
    audio: null,
    audioAnimFrame: null,

    // Preview-Bereich (Sekunden); Laenge fix 15s (DLC-Standard)
    previewStart: -1,
    previewLength: 15,
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
        canvas.addEventListener('dblclick', onDblClick);
        canvas.addEventListener('mouseleave', () => {
            state.isPanning = false;
            if (state.dragMode) endNoteDrag();
        });

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

    setEditMode(enabled) {
        state.editMode = enabled;
    },

    // ── Audio-Wiedergabe ────────────────────────────────────
    loadAudio(url) {
        this.stopAudio();
        state.audio = new Audio(url);
        state.audio.preload = 'auto';
    },

    playAudio(fromTime) {
        if (!state.audio) return false;
        if (fromTime != null && fromTime >= 0) state.audio.currentTime = fromTime;
        state.audio.play();
        syncPlayhead();
        return true;
    },

    pauseAudio() {
        if (!state.audio) return;
        state.audio.pause();
        if (state.audioAnimFrame) cancelAnimationFrame(state.audioAnimFrame);
    },

    stopAudio() {
        if (state.audio) {
            state.audio.pause();
            state.audio.currentTime = 0;
        }
        if (state.audioAnimFrame) cancelAnimationFrame(state.audioAnimFrame);
        state.playhead = 0;
        render();
    },

    isPlaying() {
        return state.audio != null && !state.audio.paused;
    },

    getAudioDuration() {
        return state.audio?.duration || 0;
    },

    // ── Preview-Bereich ─────────────────────────────────────
    setPreview(startSeconds, lengthSeconds) {
        state.previewStart = startSeconds;
        if (lengthSeconds > 0) state.previewLength = lengthSeconds;
        render();
    },

    getPreview() {
        return { start: state.previewStart, length: state.previewLength };
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
        this.stopAudio();
        state.audio = null;
        state.canvas = null;
        state.ctx = null;
    }
};

// ── Audio-Playhead-Sync ─────────────────────────────────────

function syncPlayhead() {
    if (!state.audio || state.audio.paused) return;
    state.playhead = state.audio.currentTime;

    // Auto-Scroll: Playhead im sichtbaren Bereich halten
    const x = timeToX(state.playhead);
    if (state.canvas && (x < 0 || x > state.canvas.width * 0.85)) {
        state.scrollX = state.playhead * state.pixelsPerSecond - state.canvas.width * 0.2;
        if (state.scrollX < 0) state.scrollX = 0;
    }

    render();
    state.audioAnimFrame = requestAnimationFrame(syncPlayhead);
}

// ── Canvas resize ───────────────────────────────────────────

function resizeCanvas() {
    if (!state.canvas) return;
    const parent = state.canvas.parentElement;
    state.canvas.width = parent.clientWidth;
    state.canvas.height = parent.clientHeight;
    render();
}

// ── Coordinate helpers ──────────────────────────────────────

function timeToX(t) { return t * state.pixelsPerSecond - state.scrollX; }
function xToTime(x) { return (x + state.scrollX) / state.pixelsPerSecond; }

function noteToY(midiNote) {
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

function noteMidi(n) { return (n.octave || 0) * 12 + (n.fIdx || 0) + 12; }

// Hit-Test: gibt {idx, zone} zurueck. zone: 'body' | 'edge-l' | 'edge-r'
function hitTestNote(x, y) {
    for (let i = state.notes.length - 1; i >= 0; i--) {
        const n = state.notes[i];
        const nx = timeToX(n.time);
        const nw = Math.max(n.length * state.pixelsPerSecond, 4);
        const ny = noteToY(noteMidi(n));

        if (y >= ny && y <= ny + NOTE_HEIGHT && x >= nx - 2 && x <= nx + nw + 2) {
            if (Math.abs(x - nx) <= EDGE_PX) return { idx: i, zone: 'edge-l' };
            if (Math.abs(x - (nx + nw)) <= EDGE_PX) return { idx: i, zone: 'edge-r' };
            return { idx: i, zone: 'body' };
        }
    }
    return { idx: -1, zone: null };
}

// Hit-Test fuer den Preview-Bereich (oberer Streifen, 22px hoch)
function hitTestPreview(x, y) {
    if (state.previewStart < 0) return null;
    const px = timeToX(state.previewStart);
    const pw = state.previewLength * state.pixelsPerSecond;
    if (y > 26) return null;
    if (Math.abs(x - px) <= PREVIEW_EDGE_PX) return 'preview-l';
    if (Math.abs(x - (px + pw)) <= PREVIEW_EDGE_PX) return 'preview-r';
    if (x >= px && x <= px + pw) return 'preview-move';
    return null;
}

// ── Rendering ───────────────────────────────────────────────

function render() {
    if (!state.ctx || !state.canvas) return;
    const ctx = state.ctx;
    const W = state.canvas.width;
    const H = state.canvas.height;

    ctx.clearRect(0, 0, W, H);

    drawGrid(ctx, W, H);
    drawPreviewRegion(ctx, W, H);
    drawNotes(ctx, W, H);
    drawPlayhead(ctx, W, H);
}

function drawGrid(ctx, W, H) {
    for (let note = MIN_NOTE; note <= MAX_NOTE; note++) {
        const y = noteToY(note);
        if (y < -NOTE_HEIGHT || y > H) continue;

        ctx.fillStyle = isBlackKey(note) ? '#141414' : '#1a1a1a';
        ctx.fillRect(0, y, W, NOTE_HEIGHT);

        const isCNote = note % 12 === 0;
        ctx.strokeStyle = isCNote ? '#3a3a3a' : '#222222';
        ctx.lineWidth = isCNote ? 1 : 0.5;
        ctx.beginPath();
        ctx.moveTo(0, y + NOTE_HEIGHT);
        ctx.lineTo(W, y + NOTE_HEIGHT);
        ctx.stroke();
    }

    const startTime = Math.max(0, xToTime(0));
    const endTime = xToTime(W);

    let interval = 1;
    if (state.pixelsPerSecond < 30) interval = 5;
    else if (state.pixelsPerSecond < 60) interval = 2;
    else if (state.pixelsPerSecond > 200) interval = 0.5;
    else if (state.pixelsPerSecond > 300) interval = 0.25;

    const gridStart = Math.floor(startTime / interval) * interval;
    for (let t = gridStart; t <= endTime; t += interval) {
        const x = timeToX(t);
        const isMajor = Math.abs(t % 5) < 0.001;

        ctx.strokeStyle = isMajor ? '#333333' : '#242424';
        ctx.lineWidth = isMajor ? 1 : 0.5;
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, H);
        ctx.stroke();

        if (isMajor || interval >= 1) {
            const min = Math.floor(t / 60);
            const sec = Math.floor(t % 60);
            ctx.fillStyle = '#5a5a5a';
            ctx.font = '9px "Segoe UI", sans-serif';
            ctx.fillText(`${min}:${sec.toString().padStart(2, '0')}`, x + 3, 11);
        }
    }
}

function drawPreviewRegion(ctx, W, H) {
    if (state.previewStart < 0) return;
    const x = timeToX(state.previewStart);
    const w = state.previewLength * state.pixelsPerSecond;
    if (x + w < 0 || x > W) return;

    // Transparente Flaeche ueber die volle Hoehe
    ctx.fillStyle = 'rgba(93, 194, 30, 0.07)';
    ctx.fillRect(x, 0, w, H);

    // Oberer Griff-Streifen
    ctx.fillStyle = 'rgba(93, 194, 30, 0.3)';
    ctx.fillRect(x, 0, w, 22);

    // Kanten
    ctx.fillStyle = '#5dc21e';
    ctx.fillRect(x - 1.5, 0, 3, H);
    ctx.fillRect(x + w - 1.5, 0, 3, H);

    // Label
    ctx.fillStyle = '#c8f0a8';
    ctx.font = 'bold 10px "Segoe UI", sans-serif';
    const min = Math.floor(state.previewStart / 60);
    const sec = Math.floor(state.previewStart % 60);
    ctx.fillText(
        `PREVIEW ${min}:${sec.toString().padStart(2, '0')} (+${state.previewLength}s)`,
        x + 6, 15);
}

function drawNotes(ctx, W, H) {
    for (let i = 0; i < state.notes.length; i++) {
        const n = state.notes[i];
        const x = timeToX(n.time);
        const w = n.length * state.pixelsPerSecond;
        const y = noteToY(noteMidi(n));

        if (x + w < 0 || x > W || y + NOTE_HEIGHT < 0 || y > H) continue;

        const isSelected = i === state.selectedIdx;
        const isHovered = i === state.hoveredIdx;

        let color = '#5dc21e';
        if (n.type === 'golden') color = '#ffb900';
        else if (n.type === 'freestyle') color = '#00b7c3';

        ctx.globalAlpha = isSelected ? 1.0 : isHovered ? 0.9 : 0.75;

        ctx.fillStyle = color;
        const r = 2;
        roundRect(ctx, x, y + 1, Math.max(w, 4), NOTE_HEIGHT - 2, r);
        ctx.fill();

        ctx.globalAlpha = 1;

        if (isSelected) {
            ctx.strokeStyle = '#ffffff';
            ctx.lineWidth = 1.5;
            roundRect(ctx, x, y + 1, Math.max(w, 4), NOTE_HEIGHT - 2, r);
            ctx.stroke();

            // Resize-Griffe an den Kanten
            ctx.fillStyle = '#ffffff';
            ctx.fillRect(x - 1, y + 3, 3, NOTE_HEIGHT - 6);
            ctx.fillRect(x + Math.max(w, 4) - 2, y + 3, 3, NOTE_HEIGHT - 6);
        }

        if (n.syllable && w > 12) {
            ctx.fillStyle = isSelected ? '#fff' : 'rgba(255,255,255,0.85)';
            ctx.font = '10px "Segoe UI", sans-serif';
            ctx.fillText(n.syllable, x + 3, y + NOTE_HEIGHT - 4, w - 6);
        }
    }
}

function drawPlayhead(ctx, W, H) {
    const x = timeToX(state.playhead);
    if (x < 0 || x > W) return;

    ctx.strokeStyle = '#e81123';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, H);
    ctx.stroke();

    ctx.fillStyle = '#e81123';
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
        const factor = e.deltaY > 0 ? 0.85 : 1.18;
        const mouseTime = xToTime(e.offsetX);
        state.pixelsPerSecond *= factor;
        state.pixelsPerSecond = Math.max(20, Math.min(400, state.pixelsPerSecond));
        state.scrollX = mouseTime * state.pixelsPerSecond - e.offsetX;
        if (state.scrollX < 0) state.scrollX = 0;
    } else if (e.shiftKey) {
        state.scrollY += e.deltaY * 0.5;
        state.scrollY = Math.max(0, Math.min(NOTE_RANGE * NOTE_HEIGHT - state.canvas.height + 50, state.scrollY));
    } else {
        state.scrollX += e.deltaY * 1.5;
        if (state.scrollX < 0) state.scrollX = 0;
    }

    render();
}

function onMouseDown(e) {
    if (e.button === 1 || (e.button === 0 && e.altKey)) {
        state.isPanning = true;
        state.panStartX = e.clientX;
        state.panStartScrollX = state.scrollX;
        e.preventDefault();
        return;
    }

    if (e.button !== 0) return;

    // 1. Preview-Bereich anfassen?
    const previewZone = hitTestPreview(e.offsetX, e.offsetY);
    if (previewZone) {
        state.dragMode = previewZone;
        state.dragStartTime = state.previewStart;
        state.dragStartLen = state.previewLength;
        state.dragMouseTime = xToTime(e.offsetX);
        state.dragChanged = false;
        return;
    }

    // 2. Note anfassen?
    const hit = hitTestNote(e.offsetX, e.offsetY);

    if (hit.idx !== state.selectedIdx) {
        state.selectedIdx = hit.idx;
        render();
        if (state.dotNetRef) {
            state.dotNetRef.invokeMethodAsync('OnNoteSelected', hit.idx);
        }
    }

    if (hit.idx >= 0 && state.editMode) {
        const n = state.notes[hit.idx];
        state.dragMode = hit.zone === 'edge-l' ? 'resize-l'
                       : hit.zone === 'edge-r' ? 'resize-r' : 'move';
        state.dragNoteIdx = hit.idx;
        state.dragStartTime = n.time;
        state.dragStartLen = n.length;
        state.dragStartNote = noteMidi(n);
        state.dragMouseTime = xToTime(e.offsetX);
        state.dragChanged = false;
    }
}

function onMouseMove(e) {
    if (state.isPanning) {
        const dx = e.clientX - state.panStartX;
        state.scrollX = state.panStartScrollX - dx;
        if (state.scrollX < 0) state.scrollX = 0;
        render();
        return;
    }

    // Preview-Drag
    if (state.dragMode === 'preview-move' || state.dragMode === 'preview-l' || state.dragMode === 'preview-r') {
        const dt = xToTime(e.offsetX) - state.dragMouseTime;
        if (state.dragMode === 'preview-move') {
            state.previewStart = Math.max(0, state.dragStartTime + dt);
        } else if (state.dragMode === 'preview-l') {
            const newStart = Math.max(0, state.dragStartTime + dt);
            state.previewLength = Math.max(5, state.dragStartLen + (state.dragStartTime - newStart));
            state.previewStart = newStart;
        } else {
            state.previewLength = Math.max(5, state.dragStartLen + dt);
        }
        state.dragChanged = true;
        render();
        return;
    }

    // Note-Drag
    if (state.dragMode && state.dragNoteIdx >= 0) {
        const n = state.notes[state.dragNoteIdx];
        const dt = xToTime(e.offsetX) - state.dragMouseTime;

        if (state.dragMode === 'move') {
            n.time = Math.max(0, state.dragStartTime + dt);
            // Tonhoehe folgt der Maus (vertikal)
            const targetMidi = yToNote(e.offsetY);
            if (targetMidi >= MIN_NOTE && targetMidi <= MAX_NOTE) {
                const m = targetMidi - 12;
                n.octave = Math.floor(m / 12);
                n.fIdx = m % 12;
            }
        } else if (state.dragMode === 'resize-l') {
            const newStart = Math.max(0, Math.min(state.dragStartTime + dt,
                state.dragStartTime + state.dragStartLen - 0.05));
            n.length = state.dragStartLen + (state.dragStartTime - newStart);
            n.time = newStart;
        } else if (state.dragMode === 'resize-r') {
            n.length = Math.max(0.05, state.dragStartLen + dt);
        }

        state.dragChanged = true;
        render();
        return;
    }

    // Hover + Cursor
    const previewZone = hitTestPreview(e.offsetX, e.offsetY);
    if (previewZone) {
        state.canvas.style.cursor = previewZone === 'preview-move' ? 'grab' : 'ew-resize';
        return;
    }

    const hit = hitTestNote(e.offsetX, e.offsetY);
    if (hit.idx !== state.hoveredIdx) {
        state.hoveredIdx = hit.idx;
        render();
    }
    state.canvas.style.cursor =
        hit.idx < 0 ? 'default'
        : (hit.zone === 'edge-l' || hit.zone === 'edge-r') && state.editMode ? 'ew-resize'
        : state.editMode ? 'move' : 'pointer';
}

function onMouseUp(e) {
    state.isPanning = false;
    if (state.dragMode) endNoteDrag();
}

function onDblClick(e) {
    // Doppelklick auf leere Flaeche: Playhead setzen (Audio-Scrubbing)
    const hit = hitTestNote(e.offsetX, e.offsetY);
    if (hit.idx < 0) {
        state.playhead = Math.max(0, xToTime(e.offsetX));
        if (state.audio) state.audio.currentTime = state.playhead;
        render();
    }
}

function endNoteDrag() {
    const wasPreview = state.dragMode?.startsWith('preview');
    const changed = state.dragChanged;
    const idx = state.dragNoteIdx;

    state.dragMode = null;
    state.dragNoteIdx = -1;
    state.dragChanged = false;

    if (!changed || !state.dotNetRef) return;

    if (wasPreview) {
        state.dotNetRef.invokeMethodAsync('OnPreviewChanged',
            state.previewStart, state.previewLength);
    } else if (idx >= 0) {
        const n = state.notes[idx];
        state.dotNetRef.invokeMethodAsync('OnNoteEdited', idx,
            n.time, n.length, noteMidi(n) - 12, n.syllable || '');
    }
}
