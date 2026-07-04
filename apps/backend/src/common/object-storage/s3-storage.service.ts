import { PutObjectCommand, S3Client } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { Inject, Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';

export interface PresignedUploadUrl {
  uploadUrl: string;
  expiresAt: string;
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

    this.bucket = this.config.get<string>('S3_BUCKET', 'binexus-dev');
    this.presignedTtlSeconds = Number(
      this.config.get<string>('S3_PRESIGNED_UPLOAD_TTL_SECONDS', '900'),
    );

    this.client = new S3Client({
      endpoint,
      region,
      credentials: { accessKeyId, secretAccessKey },
      forcePathStyle: true,
    });
  }

  get bucketName(): string {
    return this.bucket;
  }

  get uploadTtlSeconds(): number {
    return this.presignedTtlSeconds;
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
