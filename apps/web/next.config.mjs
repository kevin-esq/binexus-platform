/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  transpilePackages: ['@binexus/ui', '@binexus/sdk', '@binexus/types'],
  experimental: {
    typedRoutes: true,
  },
};

export default nextConfig;
