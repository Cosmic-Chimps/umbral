import type { NextConfig } from "next";

const config: NextConfig = {
  output: "standalone", // what the Dockerfile copies: the app and the few packages it needs
  poweredByHeader: false,
};

export default config;
