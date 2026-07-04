import { HeadObjectCommand, PutObjectCommand, S3Client } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import {
  BadRequestException,
  Inject,
  Injectable,
  ServiceUnavailableException,
} from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { NodeHttpHandler } from '@smithy/node-http-handler';

export interface PresignedUploadUrl {
  uploadUrl: string;
  expiresAt: string;
}

const DEFAULT_CONNECTION_TIMEOUT_MS = 3_000;
const DEFAULT_REQUEST_TIMEOUT_MS = 5_000;

function isObjectNotFound(error: unknown): boolean {
  if (!error || typeof error !== 'object') return false;
  const err = error as { name?: string; $metadata?: { httpStatusCode?: number } };
  return err.name === 'NotFound' || err.$metadata?.httpStatusCode === 404;
}

function isStorageUnavailable(error: unknown): boolean {
  if (!error || typeof error !== 'object') return false;
  const err = error as { name?: string; code?: string };
  return (
    err.name === 'TimeoutError' ||
    err.code === 'ECONNREFUSED' ||
    err.code === 'ETIMEDOUT' ||
    err.code === 'ENOTFOUND'
  );
}

@Injectable()
export class S3StorageService {
  private readonly client: S3Client;
  private readonly bucket: string;
  private readonly presignedTtlSeconds: number;

  constructor(@Inject(ConfigService) private readonly config: ConfigService) {
    const endpoint = this.config.get<string>('S3_ENDPOINT', 'http://localhost:9000');
    const region = this.config.get<string>('S3_REGION', 'us-east-1');
    const accessKeyId = this.config.get<string>('S3_ACCESS_KEY', 'binexus');
    const secretAccessKey = this.config.get<string>('S3_SECRET_KEY', 'binexus123');
    const requestTimeoutMs = Number(
      this.config.get<string>('S3_REQUEST_TIMEOUT_MS', String(DEFAULT_REQUEST_TIMEOUT_MS)),
    );

    this.bucket = this.config.get<string>('S3_BUCKET', 'binexus-dev');
    this.presignedTtlSeconds = Number(
      this.config.get<string>('S3_PRESIGNED_UPLOAD_TTL_SECONDS', '900'),
    );

    this.client = new S3Client({
      endpoint,
      region,
      credentials: { accessKeyId, secretAccessKey },
      forcePathStyle: true,
      requestHandler: new NodeHttpHandler({
        connectionTimeout: DEFAULT_CONNECTION_TIMEOUT_MS,
        requestTimeout: requestTimeoutMs,
      }),
    });
  }

  get bucketName(): string {
    return this.bucket;
  }

  get uploadTtlSeconds(): number {
    return this.presignedTtlSeconds;
  }

  async assertObjectExists(objectKey: string, fieldName: string): Promise<void> {
    try {
      await this.client.send(
        new HeadObjectCommand({
          Bucket: this.bucket,
          Key: objectKey,
        }),
      );
    } catch (error) {
      if (isObjectNotFound(error)) {
        throw new BadRequestException(`${fieldName} was not found in object storage.`);
      }
      if (isStorageUnavailable(error)) {
        throw new ServiceUnavailableException('Object storage is unavailable; try again later.');
      }
      throw new ServiceUnavailableException('Object storage is unavailable; try again later.');
    }
  }

  async createPresignedUploadUrl(
    objectKey: string,
    contentType: string,
  ): Promise<PresignedUploadUrl> {
    const command = new PutObjectCommand({
      Bucket: this.bucket,
      Key: objectKey,
      ContentType: contentType,
    });

    const uploadUrl = await getSignedUrl(this.client, command, {
      expiresIn: this.presignedTtlSeconds,
    });

    return {
      uploadUrl,
      expiresAt: new Date(Date.now() + this.presignedTtlSeconds * 1000).toISOString(),
    };
  }
}
