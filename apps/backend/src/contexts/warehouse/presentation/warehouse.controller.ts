import {
  type CompletePickingTaskResult,
  type ListPickingTasksResult,
  type PickingTaskStatus,
  type UserId,
} from '@binexus/types';
import {
  Controller,
  Get,
  Headers,
  Inject,
  Param,
  Post,
  Query,
  UnauthorizedException,
} from '@nestjs/common';
import { Type } from 'class-transformer';
import { IsIn, IsInt, IsOptional, IsString, Min } from 'class-validator';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { CurrentUser, type RequestUser } from '../../../common/decorators/current-user.decorator';
import { CompletePickingTaskCommand } from '../application/commands/complete-picking-task.command';
import { WarehouseReadService } from '../application/warehouse-read.service';

class ListPickingTasksQueryDto {
  @IsOptional()
  @IsIn(['PENDING', 'COMPLETED', 'CANCELLED'])
  status?: PickingTaskStatus;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Type(() => Number)
  limit?: number;

  @IsOptional()
  @IsString()
  cursor?: string;
}

@Controller('warehouse')
export class WarehouseController {
  constructor(
    @Inject(WarehouseReadService) private readonly warehouseRead: WarehouseReadService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @Get('picking-tasks')
  listPickingTasks(@Query() query: ListPickingTasksQueryDto): Promise<ListPickingTasksResult> {
    return this.warehouseRead.listPickingTasks({
      status: query.status,
      limit: query.limit,
      cursor: query.cursor,
    });
  }

  @Post('picking-tasks/:id/complete')
  async completePickingTask(
    @Param('id') id: string,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CompletePickingTaskResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CompletePickingTaskCommand(id, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }
}
