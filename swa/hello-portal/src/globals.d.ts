export {};

declare global {
  interface Window {
    HELLO_PORTAL_CONFIG?: {
      projectName?: string;
      apiBaseUrl?: string;
      storageWebBaseUrl?: string;
      swaBaseUrl?: string;
      appInsightsConnectionString?: string;
    };
    Microsoft?: any;
  }
}
