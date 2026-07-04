import type { DeliveryProofUploadKind } from '@binexus/types';

import { api } from './api';

export async function uploadDeliveryProofFile(
  deliveryRouteStopId: string,
  kind: DeliveryProofUploadKind,
  file: File,
): Promise<string> {
  const presigned = await api.createDeliveryProofUpload(deliveryRouteStopId, {
    kind,
    contentType: file.type,
    sizeBytes: file.size,
  });

  const response = await fetch(presigned.uploadUrl, {
    method: 'PUT',
    body: file,
    headers: {
      'Content-Type': file.type,
    },
  });

  if (!response.ok) {
    throw new Error(`Proof upload failed (${response.status})`);
  }

  return presigned.objectKey;
}
