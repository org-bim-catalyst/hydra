// specs/067 Phase 13 — admin 'defaultModels' catalog (English).
export const enAdminDefaultModels = {
  title: 'Default models',
  subtitle:
    'The model each provider contributes — a capability assigned to a provider runs on the model chosen here',
  columns: {
    provider: 'Provider',
    status: 'Status',
    defaultModel: 'Default model',
  },
  empty: 'No assignable providers found.',
  info: 'Only providers that are enabled with a credential appear here, and only models marked Available on the Providers page can be a default. A provider with no default model cannot be assigned to a capability.',
  feedback: {
    cleared: 'Default model cleared.',
    saved: 'Default model saved.',
    failed: 'Something went wrong. Please try again.',
  },
  row: {
    enabled: 'Enabled',
    disabled: 'Disabled',
    noDefault: 'No default',
    defaultModelFor: 'Default model for {provider}',
    modelsLoadFailed: "Couldn't load this provider's models. Reload the page to try again.",
    noAvailableModels: 'No Available models — mark one Available on the Providers page first.',
  },
} as const
