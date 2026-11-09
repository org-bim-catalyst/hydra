import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSpatialLayersStore, type SpatialQueryFeature } from '../../../store/spatialLayersStore'
import { FeatureAttributePanel } from './FeatureAttributePanel'

const feature: SpatialQueryFeature = {
  featureId: 'f1',
  name: 'Test Building',
  attributes: {
    height: '45.2',
    floors: 5,
    use: 'Commercial',
  },
  geometry: {
    kind: 'point',
    points: [{ latitude: 23.5, longitude: 58.3 }],
  },
}

const datasetFields = [
  { name: 'height', displayName: 'Height', unit: 'm' },
  { name: 'floors', displayName: 'Floors' },
  { name: 'use', displayName: 'Use' },
]

describe('FeatureAttributePanel', () => {
  beforeEach(() => {
    useSpatialLayersStore.getState().clearLayers()
  })

  it('renders the feature name as the title', () => {
    render(<FeatureAttributePanel feature={feature} datasetFields={datasetFields} onClose={vi.fn()} />)

    expect(screen.getByText('Test Building')).toBeInTheDocument()
  })

  it('renders attribute labels and values', () => {
    render(<FeatureAttributePanel feature={feature} datasetFields={datasetFields} onClose={vi.fn()} />)

    expect(screen.getByText('Height')).toBeInTheDocument()
    expect(screen.getByText('45.2 m')).toBeInTheDocument()
    expect(screen.getByText('Floors')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
    expect(screen.getByText('Use')).toBeInTheDocument()
    expect(screen.getByText('Commercial')).toBeInTheDocument()
  })

  it('shows the overlay rank, score, and source provenance', () => {
    const ranked: SpatialQueryFeature = {
      ...feature,
      overlay: { eligibility: { access: true, density: false }, criterionScores: { access: 0.8 }, score: 0.8, rank: 1, provenance: ['parcels', 'roads'] },
    }
    render(<FeatureAttributePanel feature={ranked} datasetFields={datasetFields} onClose={vi.fn()} />)

    expect(screen.getByText('Rank: 1')).toBeInTheDocument()
    expect(screen.getByText('Suitability score: 0.800')).toBeInTheDocument()
    expect(screen.getByText('Sources: parcels, roads')).toBeInTheDocument()
    expect(screen.getByText('access: meets criterion')).toBeInTheDocument()
    expect(screen.getByText('density: does not meet criterion')).toBeInTheDocument()
    expect(screen.getByText('access score: 0.800')).toBeInTheDocument()
  })

  it('falls back to the raw key when the field is unknown', () => {
    const withUnknown: SpatialQueryFeature = {
      ...feature,
      attributes: { mystery: 'value' },
    }

    render(<FeatureAttributePanel feature={withUnknown} datasetFields={datasetFields} onClose={vi.fn()} />)

    expect(screen.getByText('mystery')).toBeInTheDocument()
  })

  it('shows an empty-state message when there are no attributes', () => {
    const withoutAttributes: SpatialQueryFeature = { ...feature, attributes: {} }

    render(
      <FeatureAttributePanel feature={withoutAttributes} datasetFields={datasetFields} onClose={vi.fn()} />,
    )

    expect(screen.getByText('No attribute data available for this feature.')).toBeInTheDocument()
  })

  it('calls onClose when the close button is clicked', async () => {
    const onClose = vi.fn()
    render(<FeatureAttributePanel feature={feature} datasetFields={datasetFields} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: 'Close feature details' }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('does not alter layer state when opened or closed', async () => {
    const onClose = vi.fn()
    render(<FeatureAttributePanel feature={feature} datasetFields={datasetFields} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: 'Close feature details' }))

    expect(useSpatialLayersStore.getState().layers).toEqual([])
  })
})
