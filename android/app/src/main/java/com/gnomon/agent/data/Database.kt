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
}

@Database(entities = [ConfigEntity::class, RulesEntity::class, PendingDeltaEntity::class], version = 2, exportSchema = false)
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
        fun get(context: Context) = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(context, GnomonDatabase::class.java, "gnomon.db")
                .addMigrations(MIGRATION_1_2).build().also { instance = it }
        }
    }
}
