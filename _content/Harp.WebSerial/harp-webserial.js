// Web Serial bindings for Harp.WebSerial. Each open port is tracked by a handle object that owns
// the stream reader and writer, so the .NET side never touches the Web Serial streams directly.

export function isSupported() {
    return typeof navigator !== "undefined" && "serial" in navigator;
}

export async function requestPort(filtersJson) {
    const filters = filtersJson ? JSON.parse(filtersJson) : [];
    try {
        return await navigator.serial.requestPort({ filters });
    } catch (e) {
        // The user dismissed the port chooser without selecting a port.
        if (e.name === "NotFoundError") return null;
        throw e;
    }
}

export async function getPorts() {
    return await navigator.serial.getPorts();
}

export function getPortCount(ports) {
    return ports.length;
}

export function getPortAt(ports, index) {
    return ports[index];
}

export function getVendorId(port) {
    return port.getInfo().usbVendorId ?? -1;
}

export function getProductId(port) {
    return port.getInfo().usbProductId ?? -1;
}

export async function forget(port) {
    await port.forget();
}

export async function open(port, baudRate, dataBits, stopBits, parity, flowControl, bufferSize) {
    await port.open({ baudRate, dataBits, stopBits, parity, flowControl, bufferSize });
    return {
        port,
        reader: port.readable.getReader(),
        writer: port.writable.getWriter(),
        chunk: null,
        offset: 0,
        pending: null,
        error: null,
        done: false,
        lastWrite: null
    };
}

function pull(handle) {
    // A single pending read is shared by every caller, so a read abandoned on the .NET side
    // (for example after a cancellation) keeps its data for the next read instead of losing it.
    handle.pending ??= handle.reader.read().then(
        ({ value, done }) => {
            handle.pending = null;
            if (done) {
                handle.done = true;
            } else {
                handle.chunk = value;
                handle.offset = 0;
            }
        },
        error => {
            handle.pending = null;
            handle.error = error;
            // Errors such as BufferOverrunError or FramingError close the current readable stream,
            // but the port opens a new one when it can still deliver data.
            try { handle.reader.releaseLock(); } catch { }
            if (handle.port.readable) {
                handle.reader = handle.port.readable.getReader();
            } else {
                handle.done = true;
            }
        });
    return handle.pending;
}

function throwPendingError(handle) {
    const error = handle.error;
    if (error) {
        handle.error = null;
        throw error;
    }
}

// Waits until data is available and returns the number of bytes that can be taken,
// or 0 when the port has no more data to deliver.
export async function waitForData(handle) {
    while (!handle.chunk) {
        throwPendingError(handle);
        if (handle.done) return 0;
        await pull(handle);
    }
    return handle.chunk.length - handle.offset;
}

export function bytesAvailable(handle) {
    return handle.chunk ? handle.chunk.length - handle.offset : 0;
}

export function take(handle, view) {
    if (!handle.chunk) return 0;
    const count = Math.min(view.byteLength, handle.chunk.length - handle.offset);
    view.set(handle.chunk.subarray(handle.offset, handle.offset + count));
    handle.offset += count;
    if (handle.offset >= handle.chunk.length) {
        handle.chunk = null;
        handle.offset = 0;
    }
    return count;
}

export function write(handle, view) {
    // The memory view is only valid during this call, so copy the bytes before queuing them.
    const data = view.slice();
    const lastWrite = handle.writer.write(data);
    lastWrite.catch(() => { });
    handle.lastWrite = lastWrite;
}

export async function drain(handle) {
    if (handle.lastWrite) await handle.lastWrite;
}

export async function setSignals(handle, dataTerminalReady, requestToSend, breakSignal) {
    // -1 leaves a signal unchanged.
    const signals = {};
    if (dataTerminalReady >= 0) signals.dataTerminalReady = dataTerminalReady === 1;
    if (requestToSend >= 0) signals.requestToSend = requestToSend === 1;
    if (breakSignal >= 0) signals.break = breakSignal === 1;
    await handle.port.setSignals(signals);
}

export async function getSignals(handle) {
    const signals = await handle.port.getSignals();
    return (signals.dataCarrierDetect ? 1 : 0)
        | (signals.clearToSend ? 2 : 0)
        | (signals.ringIndicator ? 4 : 0)
        | (signals.dataSetReady ? 8 : 0);
}

export async function close(handle) {
    try { await handle.reader.cancel(); } catch { }
    try { handle.reader.releaseLock(); } catch { }
    try {
        await handle.writer.close();
    } catch {
        try { await handle.writer.abort(); } catch { }
    }
    try { handle.writer.releaseLock(); } catch { }
    await handle.port.close();
}
