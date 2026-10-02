export interface LevelAnalyser {
  readLevel(): number
  dispose(): void
}

/**
 * Wraps a Web Audio `AnalyserNode` on `source` and reports its RMS level (0–1) on demand. Returns
 * `null` where Web Audio isn't available (older browsers, jsdom in tests) so callers can degrade
 * to "no meter" instead of throwing.
 */
export function tryCreateLevelAnalyser(context: AudioContext, source: AudioNode): LevelAnalyser | null {
  try {
    const analyser = context.createAnalyser()
    analyser.fftSize = 256
    analyser.smoothingTimeConstant = 0.6
    source.connect(analyser)
    const data = new Uint8Array(analyser.frequencyBinCount)

    return {
      readLevel: () => {
        analyser.getByteTimeDomainData(data)
        let sumSquares = 0
        for (let i = 0; i < data.length; i++) {
          const normalized = (data[i] - 128) / 128
          sumSquares += normalized * normalized
        }
        return Math.sqrt(sumSquares / data.length)
      },
      dispose: () => analyser.disconnect(),
    }
  } catch {
    return null
  }
}
