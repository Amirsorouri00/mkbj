import type { NextConfig } from 'next';

const mediaBaseUrl = process.env.NEXT_PUBLIC_MKBJ_MEDIA_BASE_URL;
const productionMediaPattern = mediaBaseUrl
  ? (() => {
      const url = new URL(mediaBaseUrl);
      return { protocol: url.protocol.replace(':', '') as 'http' | 'https', hostname: url.hostname, port: url.port };
    })()
  : undefined;

const nextConfig: NextConfig = {
  output: 'standalone',
  transpilePackages: ['@mkbj/api-graphql'],
  images: {
    remotePatterns: [
      { protocol: 'http', hostname: 'localhost', port: '9000' },
      { protocol: 'http', hostname: '127.0.0.1', port: '9000' },
      ...(productionMediaPattern ? [productionMediaPattern] : []),
    ],
  },
};

export default nextConfig;
