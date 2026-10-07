// specs/067 Phase 13 — admin 'aiCapabilities' catalog (English).
export const enAdminAiCapabilities = {
  title: 'AI capabilities',
  subtitle: 'Choose which provider and model serves each capability',
  columns: {
    capability: 'Capability',
    provider: 'Assigned provider',
    model: 'Model',
    settings: 'Settings',
  },
  empty: 'No capabilities found.',
  info: "Each capability runs on the provider assigned here. Leave its model on “Provider default” to follow that provider's default model, or pick a different one of its models for this capability alone. Image generation must pick an image-capable model: a provider's default is a chat model and cannot draw.",
  settingsLoadFailed:
    "Couldn't load the capability settings, so every settings button is disabled.",
  retry: 'Retry',
  noProviderAssignable:
    'No provider can be assigned yet. Enable a provider with its credential on the Providers page, then give it a default model on the Default models page.',
  feedback: {
    assignmentSaved: 'Capability assignment saved.',
    settingsSaved: 'Capability settings saved.',
    failed: 'Something went wrong. Please try again.',
  },
  capabilities: {
    unknown: {
      consequence: 'No description available for this capability yet.',
    },
    Chat: {
      label: 'Chat',
      consequence:
        'Answers the user in conversation. Every other capability here is background work.',
    },
    LocationIntent: {
      label: 'Location intent',
      consequence:
        'Decides whether a message asks to view a place. Without it the viewer never moves.',
    },
    MemoryExtraction: {
      label: 'Memory extraction',
      consequence: 'Reads finished conversations for facts worth remembering.',
    },
    MemoryConflictDetection: {
      label: 'Memory conflict detection',
      consequence: 'Decides whether a new memory contradicts a stored one.',
    },
    DocumentClassification: {
      label: 'Document language and classification',
      consequence: 'Detects the language and type of an uploaded document.',
    },
    BoundaryVision: {
      label: 'Boundary vision',
      consequence:
        'Cross-checks a site boundary against satellite imagery. Currently requires Google Gemini.',
    },
    TurnOrchestration: {
      label: 'Turn orchestration',
      consequence:
        'Decides what each chat turn needs — whether to act, and which capability to run.',
    },
    ImageGeneration: {
      label: 'Image generation',
      consequence:
        'Draws images from a prompt — chat images and the site analysis map. Needs an image-capable model; it never falls back to a chat model.',
    },
  },
  row: {
    providerFor: 'Provider for {capability}',
    modelFor: 'Model for {capability}',
    noProviderAvailable: 'No AI provider available',
    selectProvider: 'Please select AI provider',
    assignProviderFirst: 'Assign a provider first',
    providerDefault: 'Provider default',
    providerDefaultWithModel: 'Provider default · {model}',
    loadingModels: 'Loading models…',
    selectImageModel: 'Please select image model',
    modelsLoadFailed: "Couldn't load this provider's models. Reload the page to try again.",
    noImageModel:
      'This provider has no Available model marked as able to produce images. Add or enable one on the Models page.',
    noAvailableModel:
      'This provider has no Available model. Mark one Available on the Models page.',
    configure: 'Configure {capability}',
    nothingToConfigure: 'Nothing to configure',
    settingsFor: 'Settings for {capability}',
  },
  dialog: {
    title: '{capability} settings',
    cancel: 'Cancel',
    save: 'Save',
    saving: 'Saving…',
    failed: 'Something went wrong. Please try again.',
  },
} as const
