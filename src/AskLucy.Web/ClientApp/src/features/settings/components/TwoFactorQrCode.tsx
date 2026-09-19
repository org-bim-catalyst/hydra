import { Alert, Box, CircularProgress } from '@mui/material'
import QRCode from 'qrcode'
import { useEffect, useState } from 'react'

const ISSUER = 'Ask Lucy'

/**
 * The server (`IdentityService.EnableTwoFactorAsync`) only mints the raw shared key — ASP.NET
 * Identity's TOTP validation never checks an issuer/label, so building the `otpauth://` URI is
 * purely a client-side presentation concern.
 */
function buildOtpAuthUri(email: string, sharedKey: string): string {
  const label = encodeURIComponent(`${ISSUER}:${email}`)
  const params = new URLSearchParams({ secret: sharedKey, issuer: ISSUER, algorithm: 'SHA1', digits: '6', period: '30' })
  return `otpauth://totp/${label}?${params.toString()}`
}

export function TwoFactorQrCode({ email, sharedKey }: { email: string; sharedKey: string }) {
  const [dataUrl, setDataUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let cancelled = false

    QRCode.toDataURL(buildOtpAuthUri(email, sharedKey), { width: 200, margin: 1 })
      .then((url) => {
        if (!cancelled) setDataUrl(url)
      })
      .catch(() => {
        if (!cancelled) setFailed(true)
      })

    return () => {
      cancelled = true
    }
  }, [email, sharedKey])

  if (failed) {
    return (
      <Alert severity="warning" sx={{ mb: 2, maxWidth: 480 }}>
        Could not generate a QR code. Enter the key above into your authenticator app manually.
      </Alert>
    )
  }

  if (!dataUrl) {
    return (
      <Box sx={{ mb: 2, width: 200, height: 200, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <CircularProgress size={24} aria-label="Generating QR code" />
      </Box>
    )
  }

  return (
    <Box sx={{ mb: 2 }}>
      <img src={dataUrl} alt="Scan this QR code with your authenticator app" width={200} height={200} />
    </Box>
  )
}
