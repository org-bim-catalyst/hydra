// Runs on the audio rendering thread (not the main thread) — the modern, non-deprecated
// replacement for ScriptProcessorNode. Served as a plain static file (not bundled by Vite)
// since AudioWorklet modules load via a real URL fetch, not the app's module graph.
class RecorderWorklet extends AudioWorkletProcessor {
  process(inputs) {
    const channel = inputs[0]?.[0]
    if (channel && channel.length > 0) {
      // Float32Array from a render quantum (128 frames) — cheap to clone and postMessage.
      this.port.postMessage(channel.slice())
    }
    return true
  }
}

registerProcessor('recorder-worklet', RecorderWorklet)
