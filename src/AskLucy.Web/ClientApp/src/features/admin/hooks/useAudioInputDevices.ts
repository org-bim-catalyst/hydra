import { useCallback, useEffect, useState } from 'react'

/**
 * Lists the browser's known microphones, refreshing on `devicechange` (a USB headset plugged in
 * or unplugged mid-session). Labels are only populated once microphone permission has been
 * granted at least once — before that every entry's `label` is an empty string.
 */
export function useAudioInputDevices(): MediaDeviceInfo[] {
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([])

  const refresh = useCallback(() => {
    navigator.mediaDevices
      ?.enumerateDevices?.()
      .then((all) => setDevices(all.filter((d) => d.kind === 'audioinput')))
      .catch(() => setDevices([]))
  }, [])

  useEffect(() => {
    refresh()
    navigator.mediaDevices?.addEventListener?.('devicechange', refresh)
    return () => navigator.mediaDevices?.removeEventListener?.('devicechange', refresh)
  }, [refresh])

  return devices
}
