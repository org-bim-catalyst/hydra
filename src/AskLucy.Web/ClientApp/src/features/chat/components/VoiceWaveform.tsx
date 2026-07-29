import { useEffect, useRef } from 'react'

interface VoiceWaveformProps {
  getLevels: () => Float32Array
  color: string
  height?: number
}

/**
 * Canvas-based live waveform, redrawn via requestAnimationFrame reading straight from the
 * recorder's level ring buffer — deliberately not React state, since audio blocks arrive
 * at ~344Hz (way past any sane re-render rate) and canvas drawing is cheap enough to just
 * do every frame directly.
 */
export function VoiceWaveform({ getLevels, color, height = 40 }: VoiceWaveformProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const ctx = canvas.getContext('2d')
    if (!ctx) return

    let frameId: number
    const dpr = window.devicePixelRatio || 1

    const resize = () => {
      const width = canvas.clientWidth
      canvas.width = width * dpr
      canvas.height = height * dpr
      ctx.scale(dpr, dpr)
    }
    resize()

    const draw = () => {
      const width = canvas.clientWidth
      ctx.clearRect(0, 0, width, height)

      const levels = getLevels()
      const barWidth = width / levels.length
      const midY = height / 2

      ctx.fillStyle = color
      for (let i = 0; i < levels.length; i++) {
        // Raw peak amplitude for normal speech volume is small (often well under 0.3) and
        // reads as a near-flat line — sqrt curve + gain makes typical speech visually
        // dynamic while still clamping silence down near the floor.
        const boosted = Math.min(1, Math.sqrt(levels[i]) * 1.6)
        const barHeight = Math.max(2, boosted * height)
        const x = i * barWidth
        ctx.fillRect(x, midY - barHeight / 2, Math.max(1, barWidth - 2), barHeight)
      }

      frameId = requestAnimationFrame(draw)
    }
    frameId = requestAnimationFrame(draw)

    return () => cancelAnimationFrame(frameId)
  }, [getLevels, color, height])

  return <canvas ref={canvasRef} style={{ width: '100%', height, display: 'block' }} />
}
