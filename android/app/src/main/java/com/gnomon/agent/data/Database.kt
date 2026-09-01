package com.gnomon.agent.data

import android.content.Context
import androidx.room.*
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import kotlinx.coroutines.flow.Flow

@Entity(tableName = "config") data class ConfigEntity(
    @PrimaryKey val id: Int = 1, val haUrl: String, val token: String, val kid: String, val device: String
)
@Entity(tableName = "rules") data class RulesEntity(@PrimaryKey val id: Int = 1, val version: Int, val json: String)
@Entity(tableName = "pending_delta") data class PendingDeltaEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0, val kid: String, val device: String,
    val category: String, val minutes: Int, val appId: String,
    val kind: String = "process", val appLabel: String = "",
    val createdAt: Long = System.currentTimeMillis()
)
@Entity(tableName = "activity_item", primaryKeys = ["kind", "itemId"]) data class ActivityItemEntity(
    val kind: String, val itemId: String, val label: String,
    val minutes: Int = 0, val lastSeen: Long = System.currentTimeMillis()
)

@Dao interface GnomonDao {
    @Query("SELECT * FROM config WHERE id = 1") suspend fun config(): ConfigEntity?
    @Insert(onConflict = OnConflictStrategy.REPLACE) suspend fun saveConfig(value: ConfigEntity)
    @Query("SELECT * FROM rules WHERE id = 1") suspend fun rules(): RulesEntity?
    @Insert(onConflict = OnConflictStrategy.REPLACE) suspend fun saveRules(value: RulesEntity)
    @Query("SELECT * FROM pending_delta ORDER BY id") suspend fun pending(): List<PendingDeltaEntity>
    @Query("SELECT COUNT(*) FROM pending_delta") suspend fun pendingCount(): Int
    @Insert suspend fun insert(value: PendingDeltaEntity): Long
    @Query("DELETE FROM pending_delta WHERE id = :id") suspend fun delete(id: Long)
    @Query("DELETE FROM pending_delta WHERE id IN (SELECT id FROM pending_delta ORDER BY id LIMIT :count)")
    suspend fun deleteOldest(count: Int)
    @Query("SELECT * FROM pending_delta ORDER BY id") fun observePending(): Flow<List<PendingDeltaEntity>>
    @Query("SELECT * FROM activity_item ORDER BY minutes DESC, label") suspend fun activity(): List<ActivityItemEntity>
    @Query("SELECT * FROM activity_item WHERE kind = :kind AND itemId = :itemId") suspend fun activity(kind: String, itemId: String): ActivityItemEntity?
    @Insert(onConflict = OnConflictStrategy.REPLACE) suspend fun saveActivity(value: ActivityItemEntity)
}

@Database(entities = [ConfigEntity::class, RulesEntity::class, PendingDeltaEntity::class, ActivityItemEntity::class], version = 3, exportSchema = false)
abstract class GnomonDatabase : RoomDatabase() {
    abstract fun dao(): GnomonDao
    companion object {
        @Volatile private var instance: GnomonDatabase? = null
        private val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE pending_delta ADD COLUMN kind TEXT NOT NULL DEFAULT 'process'")
                db.execSQL("ALTER TABLE pending_delta ADD COLUMN appLabel TEXT NOT NULL DEFAULT ''")
            }
        }
        private val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS activity_item (kind TEXT NOT NULL, itemId TEXT NOT NULL, label TEXT NOT NULL, minutes INTEGER NOT NULL, lastSeen INTEGER NOT NULL, PRIMARY KEY(kind, itemId))")
            }
        }
        fun get(context: Context) = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(context, GnomonDatabase::class.java, "gnomon.db")
                .addMigrations(MIGRATION_1_2, MIGRATION_2_3).build().also { instance = it }
        }
    }
}
