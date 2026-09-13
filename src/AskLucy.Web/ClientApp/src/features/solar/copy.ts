/**
 * Constitution §7 — every user-facing string this feature shows lives here, centralized rather
 * than scattered as literals, so i18n extraction is mechanical and no wording is duplicated (or
 * allowed to drift) between the store, the panels, the overlay and the notices. Every later
 * panel/notice task sources its strings from this module.
 */
export const copy = {
  // Time basis (FR-003)
  timeBasisUndetermined: 'Time zone could not be determined for this location — times are shown in UTC.',
  timesShownInLabel: 'Times shown in',

  // Polar cases (FR-002)
  sunNeverRises: 'The sun does not rise on this date.',
  sunNeverSets: 'The sun does not set on this date.',

  // Below horizon (FR-017)
  belowHorizonNotice: 'The sun is below the horizon — no shadows are cast.',

  // Buildings (FR-013, FR-014, FR-015)
  noBuildingsFound: 'No buildings found near this site — the sun path still applies.',
  buildingDataUnavailable: 'Building data is temporarily unavailable — the sun path still applies.',
  buildingDataLimited: (count: number) => `Building data was limited to the nearest ${count} buildings.`,
  buildingsExcluded: (count: number) =>
    count === 1 ? '1 building footprint could not be used and was excluded.' : `${count} building footprints could not be used and were excluded.`,

  // Site building / height provenance (FR-011)
  heightKnown: 'Recorded in source data',
  heightAssumed: 'Assumed — not recorded in source data',
  noSiteBuildingFound: 'No building was identified as the site building.',

  // Corrections validation (FR-027)
  invalidHeight: 'Height must be greater than 0 and no more than 1000 metres. The previous value was kept.',
  invalidGroundOffset: 'Ground offset must be between -500 and 500 metres. The previous value was kept.',
  correctionsApplyToSite: (siteLabel: string) => `These corrections apply to ${siteLabel} only.`,

  // Failure states (FR-045, FR-046, constitution §2.VIII)
  noActiveSite: 'A site must be shown in the viewer before solar analysis can open.',
  viewerUnavailable: 'Solar analysis could not be started — the 3D viewer is unavailable.',
  webglUnsupported: 'Your device does not support the 3D features solar analysis needs.',
  extensionFailure: 'Solar analysis ran into a problem and could not continue.',

  // Stated limits (FR-043, FR-044, SC-010)
  designStageStudyStatement: 'Design-stage study, not a certified analysis.',
  accuracyStatement: (positionToleranceDegrees: number) =>
    `Sun position accurate to within ${positionToleranceDegrees}°.`,
  assumedHeightsStatement: 'Building heights marked "assumed" were not recorded in the source data.',

  // Toolbar / panels
  toolbarLabel: 'Solar Analysis',
  timeControlPanelTitle: 'Time of Day',
  correctionsPanelTitle: 'Building Corrections',
  figuresPanelTitleFor: (localDate: string, localTime: string) => `Sun — ${localDate}, ${localTime}`,

  // Time control panel (FR-020, FR-021)
  playLabel: 'Play',
  stopLabel: 'Stop',
  dateLabel: 'Date',
  timeLabel: 'Time',
  speedLabel: 'Speed',

  // Corrections panel (FR-025, FR-026, FR-028)
  siteBuildingHeightLabel: 'Site building height',
  groundOffsetLabel: 'Ground offset',
  resetLabel: 'Reset to source values',

  // Figures content (contracts/solar-panels.md)
  azimuthLabel: 'Azimuth',
  altitudeLabel: 'Altitude',
  sunriseLabel: 'Sunrise',
  sunsetLabel: 'Sunset',
  dayLengthLabel: 'Day length',

  // Camera attitude widget — north and tilt, both driven by the camera rather than by time.
  cameraAttitudeLabel: 'View orientation',
  /** The widget's accessible text equivalent: the needle and bubble are decorative, so the actual
   * values have to be readable, not just visible (constitution §7). */
  cameraAttitudeReadout: (headingDegrees: number, tiltDegrees: number) =>
    `N ${Math.round(headingDegrees)}° · Tilt ${Math.round(tiltDegrees)}°`,
} as const
