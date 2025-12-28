using Moq;
using TaskManagementServices.Model;
using TaskManagementServices.Model.Interfaces;
using TaskManagementServices.Services;
using Xunit;

namespace TaskManagerTests
{
    public class TaskManagementServiceTests
    {
        private readonly Mock<ITaskRepository> _repo;
        private readonly Mock<IUserService> _users;
        private readonly Mock<INotificationService> _notif;
        private readonly Mock<IAuditService> _audit;
        private readonly TaskManagementService _service;

        public TaskManagementServiceTests()
        {
            _repo = new Mock<ITaskRepository>();
            _users = new Mock<IUserService>();
            _notif = new Mock<INotificationService>();
            _audit = new Mock<IAuditService>();

            _service = new TaskManagementService(
                _repo.Object, _users.Object, _notif.Object, _audit.Object);
        }

        #region CreateTask Tests (R1-R6)

        [Fact]
        public void CreateTask_InactiveUser_ThrowsInvalidOperationException() // R1
        {
            _users.Setup(u => u.IsActiveUser(It.IsAny<int>())).Returns(false);

            Assert.Throws<InvalidOperationException>(() =>
                _service.CreateTask(1, "Title", "Low", null));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void CreateTask_EmptyOrWhitespaceTitle_ThrowsArgumentException(string title) // R2
        {
            _users.Setup(u => u.IsActiveUser(It.IsAny<int>())).Returns(true);

            Assert.Throws<ArgumentException>(() =>
                _service.CreateTask(1, title, "Low", null));
        }

        [Fact]
        public void CreateTask_DeadlineInPast_ThrowsArgumentException() // R3
        {
            _users.Setup(u => u.IsActiveUser(It.IsAny<int>())).Returns(true);
            var pastDate = DateTime.UtcNow.AddDays(-1);

            Assert.Throws<ArgumentException>(() =>
                _service.CreateTask(1, "Title", "Low", pastDate));
        }

        [Fact]
        public void CreateTask_InvalidPriority_ThrowsArgumentException() // R4
        {
            _users.Setup(u => u.IsActiveUser(It.IsAny<int>())).Returns(true);

            Assert.Throws<ArgumentException>(() =>
                _service.CreateTask(1, "Title", "Urgent", null));
        }

        [Fact]
        public void CreateTask_Success_SavesNotifiesAndLogs() // R5
        {
            _users.Setup(u => u.IsActiveUser(1)).Returns(true);

            _service.CreateTask(1, "New Task", "High", null);

            _repo.Verify(r => r.Save(It.Is<TaskItem>(t => t.Title == "New Task")), Times.Once);
            _notif.Verify(n => n.NotifyCreated(1, "New Task"), Times.Once);
            _audit.Verify(a => a.Log("CREATE", 1, "New Task"), Times.Once);
        }

        [Fact]
        public void CreateTask_IncrementalId_AssignsUniqueIds() // R6
        {
            _users.Setup(u => u.IsActiveUser(It.IsAny<int>())).Returns(true);

            var task1 = _service.CreateTask(1, "Task 1", "Low", null);
            var task2 = _service.CreateTask(1, "Task 2", "Low", null);

            Assert.Equal(1, task1.Id);
            Assert.Equal(2, task2.Id);
        }

        #endregion

        #region CompleteTask Tests (R7-R9)

        [Fact]
        public void CompleteTask_TaskNotExistsOrWrongUser_ReturnsFalse() // R7
        {
            _repo.Setup(r => r.FindTask(99)).Returns((TaskItem)null);

            var result = _service.CompleteTask(1, 99);

            Assert.False(result);
        }

        [Fact]
        public void CompleteTask_AlreadyCompleted_ReturnsFalse() // R8
        {
            var task = new TaskItem { Id = 1, UserId = 1, IsCompleted = true };
            _repo.Setup(r => r.FindTask(1)).Returns(task);

            var result = _service.CompleteTask(1, 1);

            Assert.False(result);
        }

        [Fact]
        public void CompleteTask_Success_MarksCompletedSavesNotifiesAndLogs() // R9
        {
            var task = new TaskItem { Id = 1, UserId = 1, Title = "Finish it", IsCompleted = false };
            _repo.Setup(r => r.FindTask(1)).Returns(task);

            var result = _service.CompleteTask(1, 1);

            Assert.True(result);
            Assert.True(task.IsCompleted);
            _repo.Verify(r => r.Save(task), Times.Once);
            _notif.Verify(n => n.NotifyCompleted(1, "Finish it"), Times.Once);
            _audit.Verify(a => a.Log("COMPLETE", 1, "Finish it"), Times.Once);
        }

        #endregion

        #region DeleteTask Tests (R10-R11)

        [Fact]
        public void DeleteTask_TaskNotExistsOrWrongUser_ReturnsFalse() // R10
        {
            _repo.Setup(r => r.FindTask(1)).Returns(new TaskItem { UserId = 2 }); // Other user

            var result = _service.DeleteTask(1, 1);

            Assert.False(result);
        }

        [Fact]
        public void DeleteTask_Success_DeletesAndLogs() // R11
        {
            var task = new TaskItem { Id = 1, UserId = 1, Title = "To Delete" };
            _repo.Setup(r => r.FindTask(1)).Returns(task);

            var result = _service.DeleteTask(1, 1);

            Assert.True(result);
            _repo.Verify(r => r.Delete(1), Times.Once);
            _audit.Verify(a => a.Log("DELETE", 1, "To Delete"), Times.Once);
        }

        #endregion

        #region GetTasks Tests (R12-R13)

        [Fact]
        public void GetActiveTasks_ReturnsOnlyUncompletedUserTasks() // R12
        {
            var tasks = new List<TaskItem>
            {
                new TaskItem { UserId = 1, IsCompleted = false },
                new TaskItem { UserId = 1, IsCompleted = true },
                new TaskItem { UserId = 1, IsCompleted = false }
            };
            _repo.Setup(r => r.GetUserTasks(1)).Returns(tasks);

            var result = _service.GetActiveTasks(1);

            Assert.Equal(2, result.Count);
            Assert.All(result, t => Assert.False(t.IsCompleted));
        }

        [Fact]
        public void GetOverdueTasks_ReturnsOnlyUncompletedOverdueTasks() // R13
        {
            var tasks = new List<TaskItem>
            {
                new TaskItem { UserId = 1, IsCompleted = false, Deadline = DateTime.UtcNow.AddDays(-1) }, // Overdue
                new TaskItem { UserId = 1, IsCompleted = true, Deadline = DateTime.UtcNow.AddDays(-1) },  // Completed
                new TaskItem { UserId = 1, IsCompleted = false, Deadline = DateTime.UtcNow.AddDays(1) },  // Future
                new TaskItem { UserId = 1, IsCompleted = false, Deadline = null }                         // No deadline
            };
            _repo.Setup(r => r.GetUserTasks(1)).Returns(tasks);

            var result = _service.GetOverdueTasks(1);

            Assert.Single(result);
            Assert.False(result[0].IsCompleted);
            Assert.True(result[0].Deadline < DateTime.UtcNow);
        }

        #endregion

        #region UpdatePriority Tests (R14-R15)

        [Fact]
        public void UpdatePriority_TaskNotExistsOrWrongUser_ReturnsFalse() // R14
        {
            _repo.Setup(r => r.FindTask(1)).Returns((TaskItem)null);

            var result = _service.UpdatePriority(1, 1, "High");

            Assert.False(result);
        }

        [Fact]
        public void UpdatePriority_InvalidPriorityValue_ReturnsFalse() // R15
        {
            var task = new TaskItem { Id = 1, UserId = 1 };
            _repo.Setup(r => r.FindTask(1)).Returns(task);

            var result = _service.UpdatePriority(1, 1, "Urgent");

            Assert.False(result);
        }

        [Fact]
        public void UpdatePriority_Success_UpdatesSavesAndLogs()
        {
            var task = new TaskItem { Id = 1, UserId = 1, Priority = "Low" };
            _repo.Setup(r => r.FindTask(1)).Returns(task);

            var result = _service.UpdatePriority(1, 1, "Medium");

            Assert.True(result);
            Assert.Equal("Medium", task.Priority);
            _repo.Verify(r => r.Save(task), Times.Once);
            _audit.Verify(a => a.Log("UPDATE_PRIORITY", 1, "Medium"), Times.Once);
        }

        #endregion
    }
}